using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Mancala.Core;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Looks;
using Renegade.Play;
using Renegade.Ui;
using static Mancala.Game.BoardLayout;
using Side = Mancala.Core.Side;

namespace Mancala.Game;

/// <summary>
/// Mancala. Ties the Kalah rules to the 3D board: tap one of your pits and its marbles lift out
/// and are sown one by one into the following holes; captures fly to your store in glowing arcs;
/// an extra turn makes your store pulse. The rules decide everything first (<see cref="Kalah.Sow"/>
/// returns the exact path), then the marbles act it out. Drag sideways to spin the board. Built
/// on the studio kit (renegade-core): its HUD, Settings, tutorial, looks and fire.
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

	/// <summary>Each side's colour in the status line and score: gold near you, blue across.</summary>
	private static readonly Color SideOneColour = new("ffcc66");
	private static readonly Color SideTwoColour = new("8fd3ff");

	private readonly Kalah _game = new();
	private readonly Scoreboard _wins = new();
	private readonly List<Bead>[] _inHole = Enumerable.Range(0, Kalah.Holes).Select(_ => new List<Bead>()).ToArray();
	private readonly ImpactBank<Style> _impacts = new(Styles.Impact);
	private readonly List<Flames> _fires = new();

	private GameSettings _settings = null!;
	private Node3D _rig = null!;
	private Board _board = null!;
	private Camera3D _camera = null!;
	private SoundPool _sound = null!;
	private GameHud _hud = null!;
	private IAdService _ads = null!;
	private AdSchedule? _adSchedule;
	private SpinControl _spin = null!;
	private OmniLight3D _storeGlow = null!;

	private bool _busy;
	private bool _quiet;
	private float _escapeCheck;
	private float _lastSpinCheck;
	private Task<(int Generation, int Move)>? _thinking;
	private int _computerGeneration;
	private float _thinkTime;
	private bool _roundCounted;
	private Side? _moving;

	private bool _demo;
	private bool _quitAfterDemo;
	private string? _clip;
	private float _initialSpin;
	private bool _spinTest;

	private bool VsComputer => _settings.Mode == GameMode.PlayerVsComputer || _demo;

	private bool IsComputerTurn => !_game.IsOver && (_demo || (VsComputer && _game.Turn != Human));

	/// <summary>The board spins only while every marble is at rest: they ride along frozen in place.</summary>
	private bool CanSpin => !_busy && !_settings.FreezeBoard && _inHole.All(hole => hole.All(bead => !bead.Moving));

	// Mancala's own settings, kept in GameSettings.Extra.
	private bool ShowPitCounts => Flag("pit_numbers", true);
	private bool ShowStoreCounts => Flag("store_totals", true);

	private BeadShape Shape =>
		_settings.Extra.TryGetValue("marble_shape", out var saved) && System.Enum.TryParse(saved, out BeadShape shape) ? shape : BeadShape.Round;

	private bool Flag(string key, bool fallback) =>
		_settings.Extra.TryGetValue(key, out var saved) && bool.TryParse(saved, out bool value) ? value : fallback;

	public override void _Ready()
	{
		DevLog.Tag = "man";
		// Flight recorder: checks every sound reaches the output and logs pauses, focus and ads to user://blackbox.log.
		AudioWatch.Attach(this, Title);
		_settings = GameSettings.Load(board: Style.Wood, pieces: Style.Glass);
		// Settings from before the kit: same file, but the marbles were saved as "beads".
		var old = new ConfigFile();
		if (old.Load("user://settings.cfg") == Error.Ok && !old.HasSectionKey("settings", "pieces") && old.HasSectionKey("settings", "beads") && System.Enum.TryParse(old.GetValue("settings", "beads").AsString(), out Style beads))
			_settings.Pieces = beads;
		ReadDeveloperArgs();
		AudioServer.SetBusMute(0, _settings.Muted);
		BuildWorld();

		_sound = new SoundPool();
		AddChild(_sound);

		_adSchedule ??= new AdSchedule(_settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		_ads = AdServices.Create(this, AdUnits, _demo, new PlaceholderAds
		{
			TextColor = theme.Text,
			RewardTitle = "Watch to undo your move",
			ClaimText = "Claim undo",
			SkipText = "Skip (no undo)",
		});

		_hud = new GameHud(theme);
		AddChild(_hud);
		_hud.Build(new HudOptions(Title, "Tap a glowing pit  ·  Drag sideways to spin", PiecesLabel: "Marbles",
			PieceName: s => Styles.Solo(s).Name), _settings);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.UndoPressed += OnUndoPressed;
		_hud.RestartPressed += OnRestartPressed;
		_hud.SetTutorial(Tutorial.Pages);
		_hud.AddToggle("Pit numbers", ShowPitCounts, on =>
		{
			_settings.Extra["pit_numbers"] = on.ToString();
			_settings.Save();
			_board.ShowCounts(ShowPitCounts, ShowStoreCounts);
		});
		_hud.AddToggle("Store totals", ShowStoreCounts, on =>
		{
			_settings.Extra["store_totals"] = on.ToString();
			_settings.Save();
			_board.ShowCounts(ShowPitCounts, ShowStoreCounts);
		});
		_hud.AddChoice("Marble shape", Bead.ShapeNames, (int)Shape, index =>
		{
			_settings.Extra["marble_shape"] = ((BeadShape)index).ToString();
			_settings.Save();
			ReshapeBeads();
		});
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
		_hud.PiecesChosen += style =>
		{
			_settings.Pieces = style;
			_settings.Save();
			foreach (var bead in AllBeads())
				bead.ApplyStyle(style);
		};

		_spin = new SpinControl(_rig, () => CanSpin);
		_spin.Tapped += TryTap;
		_board.ShowCounts(ShowPitCounts, ShowStoreCounts);
		PlaceBeadsForNewGame();
		RefreshUi();
		if (_spinTest)
		{
			_ = SpinTest();
			return;
		}
		if (_clip != null)
		{
			_hud.Visible = false;
			_ = PlayClip(_clip);
			return;
		}
		if (!_demo)
			_hud.OfferTutorialOnce(_settings);
		_hud.ApplyDevFlags();
	}

	// ---------------------------------------------------------------- World

	private void BuildWorld()
	{
		// The board lies across the screen, seen from your side. Keeping the camera's width fixed
		// (rather than its height) makes the whole board fit any phone's width.
		_camera = new Camera3D { Position = new Vector3(0, 9.6f, 6.4f), Fov = 46, KeepAspect = Camera3D.KeepAspectEnum.Width };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, -0.3f, 0.25f));
		Stage.Build(this, -Thickness, new Color("1a0f09"));

		// Everything that spins with the board: the board itself, its marbles, the store glow.
		_rig = new Node3D();
		AddChild(_rig);
		_rig.RotateY(_initialSpin);
		_board = new Board();
		_board.Build(_settings.Board);
		_rig.AddChild(_board);

		_storeGlow = new OmniLight3D { OmniRange = 2.4f, LightEnergy = 0f, LightColor = new Color(1f, 0.7f, 0.3f), ShadowEnabled = false };
		_rig.AddChild(_storeGlow);

		// Phones compile each shader the first time it's drawn: draw every marble look once up front.
		Stage.WarmUp(this, -Thickness, Styles.Choices.Select(style => (Material)Materials.For(Styles.Solo(style).Look)).Append(Sparkle.Halo).ToArray());
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
				bead.Setup(_settings.Pieces, Shape);
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
		if (!_hud.MenuOpen)
			_spin.HandleInput(@event);
	}

	/// <summary>A new marble shape (Settings): every marble takes it and drops back into its hole.</summary>
	private void ReshapeBeads()
	{
		if (_busy)
			return;
		_quiet = true;
		foreach (var hole in _inHole)
		{
			for (int i = 0; i < hole.Count; i++)
			{
				var bead = hole[i];
				bead.Hold();
				bead.ApplyShape(Shape);
				bead.Position = DropPoint(bead.Hole, 0.2f + i * 0.12f);
				bead.Drop(Vector3.Zero);
			}
		}
		GetTree().CreateTimer(1.2).Timeout += () => _quiet = false;
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
		_spin.Stop();
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
			{
				_wins.Win(first: winner == Side.One);
				await SetWinnerAlight(winner);
			}
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
				_sound.PlayImpact(_impacts.Random(_settings.Pieces), bead.GlobalPosition, 0.8f, Styles.GainDb(_settings.Pieces) + 2f + index * 0.4f);
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
			Bead => _settings.Pieces,
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
		_spin.Process(dt);
	}

	public override void _PhysicsProcess(double delta)
	{
		// Now and then, put back any marble that left its hole or the board, so what you see always
		// matches the counts. That includes marbles that came to rest outside, on a rim or the flat
		// top (a bounce out of a full pit): frozen there, they would ride round with the board
		// outside their bowl. A rolling marble gets some slack; a resting one must be inside.
		_escapeCheck += (float)delta;
		if (_escapeCheck < 0.3f || _busy)
			return;
		_escapeCheck = 0f;
		// Resting marbles are only checked while the board holds still: mid-spin, positions can lag.
		bool turning = !Mathf.IsEqualApprox(_rig.Rotation.Y, _lastSpinCheck);
		_lastSpinCheck = _rig.Rotation.Y;
		foreach (var bead in AllBeads())
		{
			if (!bead.Moving && turning)
				continue;
			float slack = bead.Moving ? 0.15f : 0.02f;
			bool outside = DistanceToRim(bead.Hole, bead.Position) > slack || bead.Position.Y < -StoreDepth - 0.2f;
			if (!outside || bead.Returns >= 3)
				continue;
			bead.Returns++;
			DevLog.Write($"marble outside hole {bead.Hole} (moving={bead.Moving}) at {bead.Position}, putting it back");
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
		if (_busy || !IsComputerTurn || _clip != null)
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
		PutOutFire();
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

	/// <summary>Starting over against the computer counts as resigning (a loss), unless you watch an ad.</summary>
	private void OnRestartPressed()
	{
		if (_busy)
			return;
		if (_game.IsOver || _game.MovesMade == 0)
		{
			StartGame();
			return;
		}
		_hud.AskRestart(VsComputer && !_demo, countLoss =>
		{
			if (countLoss)
				_wins.Win(first: Human != Side.One);
			StartGame();
		}, then => _ads.ShowRewarded(rewarded: then, closed: () => { }));
	}

	// ---------------------------------------------------------------- The winner's fire

	/// <summary>
	/// The winner's store bursts into flame: every marble in it glows with fire, and flames rise
	/// from marbles spread across the store (a phone manages about a dozen fires at once).
	/// </summary>
	private async Task SetWinnerAlight(Side winner)
	{
		var marbles = _inHole[Kalah.StoreOf(winner)];
		int flames = Mathf.Min(9, marbles.Count);
		int every = Mathf.Max(1, marbles.Count / Mathf.Max(1, flames));
		for (int i = 0, lit = 0; i < marbles.Count; i++)
		{
			var bead = marbles[i];
			bool withFlames = i % every == 0 && lit < flames;
			_fires.Add(Flames.SetAlight(_rig, bead.Position, new[] { bead.Mesh }, radius: 0.2f, size: 0.8f, withLight: i == 0, particles: withFlames));
			if (withFlames)
			{
				lit++;
				await Wait(0.07f);
			}
		}
	}

	private void PutOutFire()
	{
		foreach (var fire in _fires)
			fire.Extinguish();
		_fires.Clear();
		foreach (var bead in AllBeads())
			bead.Mesh.MaterialOverlay = null;
	}

	// ---------------------------------------------------------------- Tutorial clips

	/// <summary>
	/// Plays one scripted scene for the tutorial (with --clip=name; Record-Clips.ps1 records it),
	/// then quits. The HUD is hidden so the clip shows only the board.
	/// </summary>
	private async Task PlayClip(string clip)
	{
		_settings.Mode = GameMode.PlayerVsPlayer;
		_settings.Muted = false;
		await Wait(1.6f);
		switch (clip)
		{
			case "sow":
				// Four marbles from the third pit: the last lands in your store, so you go again.
				await PlayMove(2);
				await Wait(0.8f);
				await PlayMove(5);
				break;
			case "capture":
			{
				// A real game up to its first capture, then that move on camera.
				var computer = new ComputerPlayer(Difficulty.Medium, seed: 3);
				int capture = -1;
				for (int guard = 0; guard < 60 && capture < 0 && !_game.IsOver; guard++)
				{
					capture = _game.LegalMoves().FirstOrDefault(m => _game.Clone().Sow(m).Captured > 0, -1);
					if (capture < 0)
						_game.Sow(computer.ChooseMove(_game));
				}
				_busy = true;
				await MatchBoardToRules();
				await Wait(1.2f);
				_busy = false;
				RefreshUi();
				if (capture >= 0)
					await PlayMove(capture);
				break;
			}
			default:
			{
				// A real game played most of the way, then its last moves; the winner's store burns.
				var computer = new ComputerPlayer(Difficulty.Medium, seed: 11);
				var moves = new List<int>();
				var trial = new Kalah();
				while (!trial.IsOver)
				{
					int m = computer.ChooseMove(trial);
					moves.Add(m);
					trial.Sow(m);
				}
				foreach (int m in moves.Take(moves.Count - 2))
					_game.Sow(m);
				_busy = true;
				await MatchBoardToRules();
				await Wait(1.2f);
				_busy = false;
				RefreshUi();
				foreach (int m in moves.Skip(moves.Count - 2))
				{
					await PlayMove(m);
					await Wait(0.4f);
				}
				break;
			}
		}
		await Wait(2.4f);
		GetTree().Quit();
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
		PutOutFire();
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

	private static Color ColorOf(Side side) => side == Side.One ? SideOneColour : SideTwoColour;

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
		_hud.SetRestartVisible(!_game.IsOver);

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
			_hud.SetStatus(text, winner is { } side ? ColorOf(side) : _hud.Theme.Text);
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

	/// <summary>Dev check: once the marbles settle, turn the board steadily and report any marble that leaves its hole.</summary>
	private async Task SpinTest()
	{
		await Wait(3f);
		DevLog.Write($"spin test: moving={AllBeads().Count(b => b.Moving)}");
		float worst = 0f;
		for (int frame = 0; frame < 240; frame++)
		{
			_rig.RotateY(0.05f);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			foreach (var bead in AllBeads())
				worst = Mathf.Max(worst, DistanceToRim(bead.Hole, bead.Position));
		}
		DevLog.Write($"spin test: worst distance outside a rim while turning {worst:0.00}");
		await Wait(0.5f);
		int outside = 0;
		foreach (var bead in AllBeads())
		{
			float d = DistanceToRim(bead.Hole, bead.Position);
			if (d > 0.05f || bead.Position.Y > 0.3f)
			{
				outside++;
				DevLog.Write($"spin test: marble of hole {bead.Hole} at {bead.Position} (rim {d:0.00}), frozen={bead.Freeze}");
			}
		}
		DevLog.Write($"spin test: {outside} marbles outside their holes");
		GetTree().Quit();
	}

	private SignalAwaiter Wait(float seconds) => ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	// ---------------------------------------------------------------- Developer flags

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo`
	/// (the computer plays both sides), `--quit-after-demo`, `--games-since-ad=4`,
	/// `--board=Wood`, `--beads=Glass`, `--shape=Gem`, `--spin=30` (degrees), `--spin-test`, `--clip=sow`. Style flags aren't saved.
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
				case "--spin-test":
					_spinTest = true;
					break;
				case "--shape" when System.Enum.TryParse(value, out BeadShape shape):
					_settings.Extra["marble_shape"] = shape.ToString();
					break;
				case "--beads" or "--pieces" when System.Enum.TryParse(value, out Style marbles):
					_settings.Pieces = marbles;
					break;
				case "--clip":
					_clip = value;
					break;
				case "--spin":
					_initialSpin = Mathf.DegToRad(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
					break;
			}
		}
	}
}
