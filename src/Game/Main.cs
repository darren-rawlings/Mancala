using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Mancala.Core;
using Mancala.Game.Ui;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using static Mancala.Game.BoardLayout;
using Side = Mancala.Core.Side;

namespace Mancala.Game;

/// <summary>
/// Mancala. Ties the Kalah rules to the 3D board: tap one of your pits and its marbles lift out
/// and are sown one by one into the following holes; captures fly to your store in glowing arcs;
/// an extra turn makes your store pulse. The rules decide everything first (<see cref="Kalah.Sow"/>
/// returns the exact path), then the marbles act it out. Drag sideways to spin the board.
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Mancala";

	/// <summary>
	/// Google's test ad units until the real ones exist in AdMob (see the release playbook in
	/// addons/renegade_core/docs). The game is for everyone, children included.
	/// </summary>
	private static readonly AdMobConfig AdUnits = AdMobConfig.Test;

	/// <summary>Side One is the near row; against the computer, that's you.</summary>
	private const Side Human = Side.One;

	private const float MinThinkTime = 0.7f;
	private const float SpinPerPixel = 0.008f;
	private const float SpinDamping = 1.6f;
	private const float DragThreshold = 14f;

	private readonly Kalah _game = new();
	private readonly Scoreboard _wins = new();
	private readonly List<Bead>[] _inHole = Enumerable.Range(0, Kalah.Holes).Select(_ => new List<Bead>()).ToArray();
	private readonly ImpactBank<Style> _impacts = new(Styles.Impact);

	private Settings _settings = null!;
	private Node3D _rig = null!;
	private Board _board = null!;
	private Camera3D _camera = null!;
	private SoundPool _sound = null!;
	private Hud _hud = null!;
	private IAdService _ads = null!;
	private AdSchedule? _adSchedule;
	private OmniLight3D _storeGlow = null!;

	private bool _busy;
	private bool _quiet;
	private float _escapeCheck;
	private Task<(int Generation, int Move)>? _thinking;
	private int _computerGeneration;
	private float _thinkTime;
	private bool _roundCounted;
	private Side? _moving;

	private bool _pressing;
	private bool _dragging;
	private Vector2 _pressPosition;
	private float _spinVelocity;
	private ulong _lastDragUsec;

	private bool _demo;
	private bool _quitAfterDemo;
	private float _initialSpin;

	private bool VsComputer => _settings.Mode == GameMode.PlayerVsComputer || _demo;

	private bool IsComputerTurn => !_game.IsOver && (_demo || (VsComputer && _game.Turn != Human));

	/// <summary>The board spins only while every marble is at rest: they ride along frozen in place.</summary>
	private bool CanSpin => !_busy && _inHole.All(hole => hole.All(bead => !bead.Moving));

	public override void _Ready()
	{
		DevLog.Tag = "man";
		_settings = Settings.Load();
		ReadDeveloperArgs();
		AudioServer.SetBusMute(0, _settings.Muted);
		BuildWorld();

		_sound = new SoundPool();
		AddChild(_sound);

		_adSchedule ??= new AdSchedule(_settings.GamesSinceAd);
		_ads = AdServices.Create(this, AdUnits, _demo, new PlaceholderAds
		{
			TextColor = Palette.Text,
			RewardTitle = "Watch to undo your move",
			ClaimText = "Claim undo",
			SkipText = "Skip (no undo)",
		});

		_hud = new Hud();
		AddChild(_hud);
		_hud.Build(Title, _settings);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.UndoPressed += OnUndoPressed;
		_hud.MuteToggled += muted =>
		{
			_settings.Muted = muted;
			AudioServer.SetBusMute(0, muted);
			_settings.Save();
		};
		_hud.ModeChosen += (mode, difficulty) =>
		{
			_settings.Mode = mode;
			_settings.Difficulty = difficulty;
			_settings.Save();
			_wins.Reset();
			StartGame();
		};
		_hud.BoardChosen += style =>
		{
			_settings.Board = style;
			_settings.Save();
			_board.ApplyStyle(style);
		};
		_hud.BeadsChosen += style =>
		{
			_settings.Beads = style;
			_settings.Save();
			foreach (var bead in AllBeads())
				bead.ApplyStyle(style);
		};

		PlaceBeadsForNewGame();
		RefreshUi();
	}

	// ---------------------------------------------------------------- World

	private void BuildWorld()
	{
		// The board lies across the screen, seen from your side. Keeping the camera's width fixed
		// (rather than its height) makes the whole board fit any phone's width.
		_camera = new Camera3D { Position = new Vector3(0, 9.6f, 6.4f), Fov = 46, KeepAspect = Camera3D.KeepAspectEnum.Width };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, -0.3f, 0.25f));

		AddChild(new DirectionalLight3D
		{
			ShadowEnabled = true,
			LightEnergy = 1.15f,
			LightColor = new Color("fff1dc"),
			RotationDegrees = new Vector3(-60, 35, 0),
		});
		AddChild(new OmniLight3D { Position = new Vector3(-4, 4, 4), OmniRange = 16, LightEnergy = 0.5f, LightColor = new Color("ffcf9a") });
		AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color("1a0f09"),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color("6e5444"),
				AmbientLightEnergy = 0.9f,
				// Something for metal and glass to reflect: without it, silver marbles and a steel board
				// render nearly black. The sky is only reflected, never seen behind the table.
				ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
				Sky = new Sky
				{
					SkyMaterial = new ProceduralSkyMaterial
					{
						SkyTopColor = new Color("8a6a52"),
						SkyHorizonColor = new Color("e8c9a0"),
						GroundBottomColor = new Color("2a1a10"),
						GroundHorizonColor = new Color("6e4c34"),
						SunAngleMax = 30,
					},
				},
			},
		});

		var table = new StaticBody3D { Position = new Vector3(0, -Thickness, 0) };
		table.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(160, 160) }, MaterialOverride = Looks.For(Styles.BoardLook(Style.Table)) });
		table.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
		AddChild(table);

		// Everything that spins with the board: the board itself, its marbles, the store glow.
		_rig = new Node3D();
		AddChild(_rig);
		_rig.RotateY(_initialSpin);
		_board = new Board();
		_board.Build(_settings.Board);
		_rig.AddChild(_board);

		_storeGlow = new OmniLight3D { OmniRange = 2.4f, LightEnergy = 0f, LightColor = new Color(1f, 0.7f, 0.3f), ShadowEnabled = false };
		_rig.AddChild(_storeGlow);

		WarmUpShaders();
	}

	/// <summary>
	/// Phones compile each shader the first time it's drawn, which stalls that frame. Draw the
	/// capture glow, the sparks and every marble material once at startup, hidden under the table.
	/// </summary>
	private void WarmUpShaders()
	{
		var warm = new Node3D { Position = new Vector3(0, -Thickness - 1f, 0) };
		AddChild(warm);
		foreach (var style in Styles.Choices)
		{
			warm.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = BeadRadius, Height = BeadRadius * 2 }, MaterialOverride = Looks.For(Styles.BeadLook(style)), MaterialOverlay = Sparkle.Halo });
			warm.AddChild(new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = Looks.For(Styles.BoardLook(style)) });
		}
		warm.AddChild(new Sparkle());
		GetTree().CreateTimer(0.4).Timeout += warm.QueueFree;
	}

	/// <summary>A random spot over a hole (board space), from where a dropped marble falls in.</summary>
	private static Vector3 DropPoint(int hole, float height = 0.45f)
	{
		var center = HoleCenter(hole);
		float spreadX = Kalah.IsStore(hole) ? StoreHalfX * 0.5f : PitRadius * 0.35f;
		float spreadZ = Kalah.IsStore(hole) ? StoreHalfZ * 0.6f : PitRadius * 0.35f;
		return center + new Vector3((float)GD.RandRange(-spreadX, spreadX), height, (float)GD.RandRange(-spreadZ, spreadZ));
	}

	private IEnumerable<Bead> AllBeads() => _inHole.SelectMany(hole => hole);

	private void PlaceBeadsForNewGame()
	{
		_quiet = true;
		foreach (int pit in Kalah.PitsOf(Side.One).Concat(Kalah.PitsOf(Side.Two)))
		{
			for (int i = 0; i < _game.StonesPerPit; i++)
			{
				var bead = new Bead();
				bead.Setup(_settings.Beads);
				bead.Landed += OnBeadLanded;
				bead.Position = DropPoint(pit, 0.15f + i * 0.26f);
				bead.Hole = pit;
				_rig.AddChild(bead);
				bead.Drop(Vector3.Zero);
				_inHole[pit].Add(bead);
			}
		}
		GetTree().CreateTimer(1.2).Timeout += () => _quiet = false;
		RefreshCounts();
	}

	// ---------------------------------------------------------------- Input

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_hud.MenuOpen)
			return;
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
				_pressing = true;
				_dragging = false;
				_pressPosition = press.Position;
				_spinVelocity = 0f;
				break;

			case InputEventMouseButton { ButtonIndex: MouseButton.Left } release:
				if (_pressing && !_dragging)
					TryTap(release.Position);
				_pressing = false;
				_dragging = false;
				break;

			case InputEventMouseMotion motion:
				if (_pressing && !_dragging && motion.Position.DistanceTo(_pressPosition) > DragThreshold && CanSpin)
				{
					_dragging = true;
					_lastDragUsec = Time.GetTicksUsec();
				}
				if (_dragging)
					Drag(motion.Relative.X);
				break;
		}
	}

	private void Drag(float pixels)
	{
		if (!CanSpin)
			return;
		float angle = pixels * SpinPerPixel;
		_rig.RotateY(angle);
		ulong now = Time.GetTicksUsec();
		float dt = Mathf.Max((now - _lastDragUsec) / 1_000_000f, 0.001f);
		_lastDragUsec = now;
		// Remember how fast the finger was moving, so a flick keeps the board turning.
		_spinVelocity = Mathf.Lerp(_spinVelocity, angle / dt, 0.5f);
	}

	private void TryTap(Vector2 screen)
	{
		if (_busy || IsComputerTurn || _game.IsOver)
			return;
		var from = _camera.ProjectRayOrigin(screen);
		var direction = _camera.ProjectRayNormal(screen);
		if (direction.Y > -0.01f)
			return;
		// Aim at the bottom of the bowls rather than the board top, which suits an angled view.
		var world = from + direction * ((-0.2f - from.Y) / direction.Y);
		int pit = Board.PitAt(_rig.ToLocal(world));
		if (pit >= 0 && _game.CanSow(pit))
			_ = PlayMove(pit);
	}

	// ---------------------------------------------------------------- Turns

	private async Task PlayMove(int pit)
	{
		_busy = true;
		_spinVelocity = 0f;
		_board.HighlightPits(null);
		_hud.SetUndoVisible(false);
		_hud.HideHint();
		var result = _game.Sow(pit);
		DevLog.Write($"frame {Engine.GetProcessFrames()}: {result.Mover} sows {pit}: {result.Picked} stones, extra={result.ExtraTurn}, capture={result.Captured}, over={result.GameOver}");
		// Until the marbles finish moving, the status stays with the mover, not the next player.
		_moving = result.Mover;
		RefreshUi();

		await SowBeads(result);
		var color = ColorOf(result.Mover);
		if (result.Captured > 0)
		{
			_hud.SetStatus($"Capture! +{result.Captured}", color);
			await CaptureBeads(result);
		}
		else if (result.ExtraTurn && !result.GameOver)
		{
			_hud.SetStatus("Go again!", color);
			PulseStore(Kalah.StoreOf(result.Mover));
			await Wait(0.5f);
		}
		_moving = null;
		if (result.GameOver)
			await SweepBeads(result.FinalSweep);

		if (_game.IsOver && !_roundCounted)
		{
			_roundCounted = true;
			if (_game.Winner is { } winner)
				_wins.Win(first: winner == Side.One);
		}
		_busy = false;
		RefreshUi();
	}

	/// <summary>The marbles lift out together, then the cluster travels hole to hole, dropping one each time.</summary>
	private async Task SowBeads(SowResult result)
	{
		var hand = new List<Bead>(_inHole[result.From]);
		_inHole[result.From].Clear();
		RefreshCounts();

		var lift = CreateTween().SetParallel();
		for (int i = 0; i < hand.Count; i++)
		{
			hand[i].Hold();
			lift.TweenProperty(hand[i], "position", HoverPoint(result.From, i), 0.22f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		}
		await ToSignal(lift, Tween.SignalName.Finished);

		for (int step = 0; step < result.Path.Count; step++)
		{
			int hole = result.Path[step];
			var move = CreateTween().SetParallel();
			for (int i = step; i < hand.Count; i++)
				move.TweenProperty(hand[i], "position", HoverPoint(hole, i - step), 0.13f).SetTrans(Tween.TransitionType.Sine);
			await ToSignal(move, Tween.SignalName.Finished);

			var bead = hand[step];
			bead.Position = DropPoint(hole, 0.32f);
			bead.Drop(new Vector3(0, -1.5f, 0));
			bead.Hole = hole;
			_inHole[hole].Add(bead);
			RefreshCounts();
		}
		await Wait(0.35f);
	}

	/// <summary>Where a carried marble floats: a loose little cluster above a hole.</summary>
	private static Vector3 HoverPoint(int hole, int index)
	{
		float angle = index * 2.4f;
		float radius = 0.05f + 0.03f * Mathf.Sqrt(index);
		return HoleCenter(hole) + new Vector3(Mathf.Cos(angle) * radius, 0.65f + index * 0.015f, Mathf.Sin(angle) * radius);
	}

	/// <summary>
	/// The capture: sparks burst from the pit, then the marbles flare with light and fly to the
	/// store in glowing arcs trailing sparks, landing with a rising run of clinks.
	/// </summary>
	private async Task CaptureBeads(SowResult result)
	{
		int store = Kalah.StoreOf(result.Mover);
		int last = result.Path[^1];
		var taken = _inHole[result.CaptureFrom].Concat(_inHole[last]).ToList();
		_inHole[result.CaptureFrom].Clear();
		_inHole[last].Clear();
		Sparkle.Burst(_rig, HoleCenter(result.CaptureFrom) + new Vector3(0, 0.3f, 0));

		for (int i = 0; i < taken.Count; i++)
		{
			var bead = taken[i];
			bead.Hold();
			bead.Mesh.MaterialOverlay = Sparkle.Halo;
			var sparkle = new Sparkle();
			bead.AddChild(sparkle);

			var start = bead.Position;
			var end = DropPoint(store, 0.4f);
			var peak = (start + end) / 2 + new Vector3(0, 2.2f, 0);
			var flight = CreateTween();
			flight.TweenInterval(0.25f + i * 0.07f);
			flight.TweenMethod(Callable.From<float>(t =>
			{
				// A quadratic Bézier: up, over and down into the store.
				var a = start.Lerp(peak, t);
				var b = peak.Lerp(end, t);
				bead.Position = a.Lerp(b, t);
			}), 0f, 1f, 0.55f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			int index = i;
			flight.TweenCallback(Callable.From(() =>
			{
				bead.Hole = store;
				_inHole[store].Add(bead);
				bead.Drop(new Vector3(0, -1f, 0));
				sparkle.Finish();
				RefreshCounts();
				// Each landing a little louder, so a big capture rings up like a payout.
				_sound.PlayImpact(_impacts.Random(_settings.Beads), bead.GlobalPosition, 0.8f, Styles.GainDb(_settings.Beads) + 2f + index * 0.4f);
				GetTree().CreateTimer(0.6).Timeout += () => bead.Mesh.MaterialOverlay = null;
			}));
		}
		PulseStore(store);
		await Wait(0.25f + taken.Count * 0.07f + 0.55f + 0.4f);
	}

	/// <summary>The store lights up warm for a moment: an extra turn, or a capture arriving.</summary>
	private void PulseStore(int store)
	{
		_storeGlow.Position = HoleCenter(store) + new Vector3(0, 0.9f, 0);
		var tween = CreateTween();
		tween.TweenProperty(_storeGlow, "light_energy", 3.5f, 0.15f);
		tween.TweenProperty(_storeGlow, "light_energy", 0f, 0.7f);
	}

	/// <summary>Game over: the marbles left on each side slide into their owner's store.</summary>
	private async Task SweepBeads(IReadOnlyList<(int Hole, int Stones)> sweep)
	{
		if (sweep.Count == 0)
			return;
		await Wait(0.3f);
		int i = 0;
		foreach (var (hole, _) in sweep)
		{
			int store = Kalah.StoreOf(Kalah.OwnerOf(hole));
			foreach (var bead in _inHole[hole].ToList())
				Carry(bead, store, i++ * 0.05f);
			_inHole[hole].Clear();
		}
		RefreshCounts();
		await Wait(0.6f + i * 0.05f);
	}

	/// <summary>Lifts a marble and drops it into another hole (used to rearrange the board).</summary>
	private void Carry(Bead bead, int hole, float delay)
	{
		bead.Hold();
		bead.Hole = hole;
		if (!_inHole[hole].Contains(bead))
			_inHole[hole].Add(bead);
		var target = DropPoint(hole, 0.5f);
		var tween = CreateTween();
		tween.TweenInterval(delay);
		tween.TweenProperty(bead, "position", bead.Position with { Y = 0.8f }, 0.15f);
		tween.TweenProperty(bead, "position", target, 0.3f).SetTrans(Tween.TransitionType.Sine);
		tween.TweenCallback(Callable.From(() => bead.Drop(new Vector3(0, -1f, 0))));
	}

	/// <summary>
	/// Moves marbles so the board matches the rules again, carrying each surplus marble to a hole
	/// that's short. Used for a new game and after Undo.
	/// </summary>
	private async Task MatchBoardToRules()
	{
		var spare = new List<Bead>();
		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			while (_inHole[hole].Count > _game[hole])
			{
				var bead = _inHole[hole][^1];
				_inHole[hole].RemoveAt(_inHole[hole].Count - 1);
				spare.Add(bead);
			}
		}
		int moved = 0;
		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			while (_inHole[hole].Count < _game[hole] && spare.Count > 0)
			{
				var bead = spare[^1];
				spare.RemoveAt(spare.Count - 1);
				Carry(bead, hole, moved++ * 0.03f);
			}
		}
		RefreshCounts();
		await Wait(0.5f + moved * 0.03f);
	}

	/// <summary>A marble on a marble sounds of the marbles; on the board, of the board; off the board, of the table.</summary>
	private void OnBeadLanded(Bead bead, float speed, Node other)
	{
		if (_quiet || speed < 0.6f)
			return;
		var surface = other switch
		{
			Bead => _settings.Beads,
			_ when other == _board.Body => _board.Style,
			_ => Style.Table,
		};
		_sound.PlayImpact(_impacts.Random(surface), bead.GlobalPosition, Mathf.Clamp(speed / 5f, 0.15f, 1f), Styles.GainDb(surface));
	}

	// ---------------------------------------------------------------- Per frame

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		UpdateComputer(dt);
		if (!_dragging && _spinVelocity != 0f)
		{
			if (!CanSpin || Mathf.Abs(_spinVelocity) < 0.02f)
				_spinVelocity = 0f;
			_rig.RotateY(_spinVelocity * dt);
			_spinVelocity *= Mathf.Exp(-SpinDamping * dt);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Now and then, put back any marble that rolled out of its hole or off the board, so what
		// you see always matches the counts.
		_escapeCheck += (float)delta;
		if (_escapeCheck < 0.5f || _busy)
			return;
		_escapeCheck = 0f;
		foreach (var bead in AllBeads())
		{
			if (!bead.Moving)
				continue;
			bool outside = DistanceToRim(bead.Hole, bead.Position) > 0.15f || bead.Position.Y < -StoreDepth - 0.2f;
			if (!outside)
				continue;
			DevLog.Write($"marble escaped from hole {bead.Hole}, putting it back");
			bead.Hold();
			bead.Position = DropPoint(bead.Hole, 0.5f);
			bead.Drop(Vector3.Zero);
		}
	}

	/// <summary>
	/// The computer thinks on a background thread (so a deep search never stalls a frame), then
	/// plays after a short natural pause. A generation number discards a search that finishes after
	/// the game was reset or undone.
	/// </summary>
	private void UpdateComputer(float dt)
	{
		if (_busy || !IsComputerTurn)
			return;
		if (_thinking == null)
		{
			var player = new ComputerPlayer(_demo ? Difficulty.Medium : _settings.Difficulty);
			var snapshot = _game.Clone();
			int generation = _computerGeneration;
			_thinking = Task.Run(() =>
			{
				var watch = System.Diagnostics.Stopwatch.StartNew();
				int move = player.ChooseMove(snapshot);
				DevLog.Write($"computer ({player.Difficulty}) chose pit {move} in {watch.ElapsedMilliseconds} ms");
				return (generation, move);
			});
			_thinkTime = 0f;
			RefreshUi();
			return;
		}
		_thinkTime += dt;
		if (!_thinking.IsCompleted || _thinkTime < (_demo ? 0.25f : MinThinkTime))
			return;
		var (forGeneration, pit) = _thinking.Result;
		_thinking = null;
		if (forGeneration == _computerGeneration && _game.CanSow(pit))
			_ = PlayMove(pit);
	}

	// ---------------------------------------------------------------- Games, undo, ads

	private void OnNewGamePressed()
	{
		if (_busy)
			return;
		bool adDue = _adSchedule!.GameFinished();
		_settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		_settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			_ads.ShowInterstitial(StartGame);
		}
		else
		{
			StartGame();
		}
	}

	private async void StartGame()
	{
		_computerGeneration++;
		_thinking = null;
		_busy = true;
		_roundCounted = false;
		_game.Reset();
		RefreshUi();
		await MatchBoardToRules();
		_busy = false;
		RefreshUi();
	}

	/// <summary>Undo is offered against the computer on your turn (or after the game ends), once you've moved.</summary>
	private bool CanUndo =>
		VsComputer && !_demo && !_busy && _game.MovesMade > 0 && (_game.IsOver || _game.Turn == Human);

	/// <summary>Undo costs a rewarded ad: it only happens if the player watches it through.</summary>
	private void OnUndoPressed()
	{
		if (!CanUndo)
			return;
		DevLog.Write("ad: rewarded (undo)");
		_hud.SetUndoVisible(false);
		_ads.ShowRewarded(rewarded: () => _ = UndoLastTurn(), closed: RefreshUi);
	}

	/// <summary>Takes back the computer's replies and then your whole last turn (extra turns included).</summary>
	private async Task UndoLastTurn()
	{
		_computerGeneration++;
		_thinking = null;
		if (_game.IsOver && _roundCounted && _game.Winner is { } winner)
			_wins.Unwin(first: winner == Side.One);
		_roundCounted = false;
		while (_game.LastMover == Kalah.Opponent(Human))
			_game.Undo();
		while (_game.LastMover == Human)
			_game.Undo();
		DevLog.Write($"undo: {_game.MovesMade} moves left, {_game.Turn} to play");
		_busy = true;
		RefreshUi();
		await MatchBoardToRules();
		_busy = false;
		RefreshUi();
	}

	// ---------------------------------------------------------------- UI

	private string NameOf(Side side) => VsComputer && !_demo
		? (side == Human ? "You" : "Computer")
		: (side == Side.One ? "Gold" : "Blue");

	private static Color ColorOf(Side side) => side == Side.One ? Palette.SideOne : Palette.SideTwo;

	private void RefreshCounts()
	{
		for (int hole = 0; hole < Kalah.Holes; hole++)
			_board.SetCount(hole, _inHole[hole].Count);
	}

	private void RefreshUi()
	{
		_hud.SetScore($"{NameOf(Side.One)}  {_wins.First}   —   {_wins.Second}  {NameOf(Side.Two)}");
		_hud.SetNewGameVisible(_game.IsOver && !_busy);
		_hud.SetUndoVisible(CanUndo);

		if (_game.IsOver)
		{
			if (_busy)
				return;
			var winner = _game.Winner;
			string text = winner switch
			{
				null => $"A draw, {_game.Store(Side.One)} each!",
				{ } w when NameOf(w) == "You" => $"You win, {_game.Store(w)} to {_game.Store(Kalah.Opponent(w))}!",
				{ } w => $"{NameOf(w)} wins, {_game.Store(w)} to {_game.Store(Kalah.Opponent(w))}",
			};
			_hud.SetStatus(text, winner is { } side ? ColorOf(side) : Palette.Text);
			_board.HighlightPits(null);
			if (_demo && _quitAfterDemo)
				GetTree().CreateTimer(1.5).Timeout += () => GetTree().Quit();
			return;
		}

		var turn = _moving ?? _game.Turn;
		string status = _moving == null && IsComputerTurn && !_demo ? "Computer is thinking…" : NameOf(turn) == "You" ? "Your turn" : $"{NameOf(turn)}'s turn";
		_hud.SetStatus(status, ColorOf(turn));
		_board.HighlightPits(_busy || IsComputerTurn ? null : _game.LegalMoves());
	}

	private SignalAwaiter Wait(float seconds) => ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	// ---------------------------------------------------------------- Developer flags

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo`
	/// (the computer plays both sides), `--quit-after-demo`, `--games-since-ad=4`,
	/// `--board=Wood`, `--beads=Glass`, `--spin=30` (degrees). Style flags aren't saved.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					_demo = true;
					_settings.Muted = false;
					break;
				case "--quit-after-demo":
					_quitAfterDemo = true;
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--board" when System.Enum.TryParse(value, out Style board):
					_settings.Board = board;
					break;
				case "--beads" when System.Enum.TryParse(value, out Style beads):
					_settings.Beads = beads;
					break;
				case "--spin":
					_initialSpin = Mathf.DegToRad(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
					break;
			}
		}
	}
}
