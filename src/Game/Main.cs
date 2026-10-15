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
/// Pit Stop: Mancala. Ties the Kalah rules to the 3D board: tap one of your pits and its gems lift
/// out and are sown one by one into the following holes; captures fly to your store in a glowing
/// arc ("the pit stop"); an extra turn makes your store pulse. The rules decide everything first
/// (<see cref="Kalah.Sow"/> returns the exact path), then the gems act it out.
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Pit Stop";

	/// <summary>
	/// Google's test ad units until the real ones exist in AdMob (see the release playbook in
	/// addons/renegade_core/docs). The game is for everyone, children included.
	/// </summary>
	private static readonly AdMobConfig AdUnits = AdMobConfig.Test;

	/// <summary>Side One is the player at the bottom-right; against the computer, that's you.</summary>
	private const Side Human = Side.One;

	private const float MinThinkTime = 0.7f;

	private readonly Kalah _game = new();
	private readonly Scoreboard _wins = new();
	private readonly List<Stone>[] _inHole = Enumerable.Range(0, Kalah.Holes).Select(_ => new List<Stone>()).ToArray();
	private readonly ImpactBank<bool> _clinks = new(onGem => onGem ? Looks.GemOnGem : Looks.GemOnWood);

	private Settings _settings = null!;
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

	private bool _demo;
	private bool _quitAfterDemo;

	private bool VsComputer => _settings.Mode == GameMode.PlayerVsComputer || _demo;

	private bool IsComputerTurn => !_game.IsOver && (_demo || (VsComputer && _game.Turn != Human));

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

		PlaceStonesForNewGame();
		RefreshUi();
	}

	// ---------------------------------------------------------------- World

	private void BuildWorld()
	{
		// Looking down the board from just past Two's store, steep enough to see into every pit.
		_camera = new Camera3D { Position = new Vector3(0, 11.2f, 8.4f), Fov = 52 };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, -0.2f, 1.55f));

		AddChild(new DirectionalLight3D
		{
			ShadowEnabled = true,
			LightEnergy = 1.15f,
			LightColor = new Color("fff1dc"),
			RotationDegrees = new Vector3(-60, 35, 0),
		});
		AddChild(new OmniLight3D { Position = new Vector3(-3, 4, 4), OmniRange = 14, LightEnergy = 0.5f, LightColor = new Color("ffcf9a") });
		AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color("1a0f09"),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color("6e5444"),
				AmbientLightEnergy = 0.9f,
			},
		});

		_board = new Board();
		_board.Build();
		AddChild(_board);

		_storeGlow = new OmniLight3D { OmniRange = 2.2f, LightEnergy = 0f, LightColor = new Color(1f, 0.7f, 0.3f), ShadowEnabled = false };
		AddChild(_storeGlow);

		WarmUpShaders();
	}

	/// <summary>
	/// Phones compile each shader the first time it's drawn, which stalls that frame. Draw the
	/// capture glow and sparks once at startup, hidden under the table, so the first capture is smooth.
	/// </summary>
	private void WarmUpShaders()
	{
		var warm = new Node3D { Position = new Vector3(0, -Thickness - 1f, 0) };
		AddChild(warm);
		warm.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = StoneRadius, Height = StoneHeight }, MaterialOverride = Looks.Gem(0), MaterialOverlay = Sparkle.Halo });
		warm.AddChild(new Sparkle());
		GetTree().CreateTimer(0.4).Timeout += warm.QueueFree;
	}

	/// <summary>A random resting spot over a hole, from where a dropped gem falls in.</summary>
	private static Vector3 DropPoint(int hole, float height = 0.45f)
	{
		var center = HoleCenter(hole);
		float spreadX = Kalah.IsStore(hole) ? StoreHalfWidth * 0.45f : PitRadius * 0.4f;
		float spreadZ = Kalah.IsStore(hole) ? StoreHalfLength * 0.6f : PitRadius * 0.4f;
		return center + new Vector3((float)GD.RandRange(-spreadX, spreadX), height, (float)GD.RandRange(-spreadZ, spreadZ));
	}

	private void PlaceStonesForNewGame()
	{
		_quiet = true;
		int color = GD.RandRange(0, Looks.GemColors.Length - 1);
		foreach (int pit in Kalah.PitsOf(Side.One).Concat(Kalah.PitsOf(Side.Two)))
		{
			for (int i = 0; i < _game.StonesPerPit; i++)
			{
				var stone = new Stone();
				stone.Setup(color++);
				stone.Landed += OnStoneLanded;
				stone.Position = DropPoint(pit, 0.2f + i * 0.12f);
				stone.Hole = pit;
				AddChild(stone);
				stone.Drop(Vector3.Zero);
				_inHole[pit].Add(stone);
			}
		}
		GetTree().CreateTimer(1.0).Timeout += () => _quiet = false;
		RefreshCounts();
	}

	// ---------------------------------------------------------------- Input

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
			return;
		if (_busy || _hud.MenuOpen || IsComputerTurn || _game.IsOver)
			return;
		var from = _camera.ProjectRayOrigin(click.Position);
		var direction = _camera.ProjectRayNormal(click.Position);
		if (direction.Y > -0.01f)
			return;
		// Aim at the bottom of the bowls rather than the board top, which suits a steep view.
		var point = from + direction * ((-0.15f - from.Y) / direction.Y);
		int pit = Board.PitAt(point);
		if (pit >= 0 && _game.CanSow(pit))
			_ = PlayMove(pit);
	}

	// ---------------------------------------------------------------- Turns

	private async Task PlayMove(int pit)
	{
		_busy = true;
		_board.HighlightPits(null);
		_hud.SetUndoVisible(false);
		var result = _game.Sow(pit);
		DevLog.Write($"frame {Engine.GetProcessFrames()}: {result.Mover} sows {pit}: {result.Picked} stones, extra={result.ExtraTurn}, capture={result.Captured}, over={result.GameOver}");
		// Until the gems finish moving, the status stays with the mover, not the next player.
		_moving = result.Mover;
		RefreshUi();

		await SowStones(result);
		var color = ColorOf(result.Mover);
		if (result.Captured > 0)
		{
			_hud.SetStatus($"Pit stop! +{result.Captured}", color);
			await CaptureStones(result);
		}
		else if (result.ExtraTurn && !result.GameOver)
		{
			_hud.SetStatus("Go again!", color);
			PulseStore(Kalah.StoreOf(result.Mover));
			await Wait(0.5f);
		}
		_moving = null;
		if (result.GameOver)
			await SweepStones(result.FinalSweep);

		if (_game.IsOver && !_roundCounted)
		{
			_roundCounted = true;
			if (_game.Winner is { } winner)
				_wins.Win(first: winner == Side.One);
		}
		_busy = false;
		RefreshUi();
	}

	/// <summary>The gems lift out together, then the cluster travels hole to hole, dropping one each time.</summary>
	private async Task SowStones(SowResult result)
	{
		var hand = new List<Stone>(_inHole[result.From]);
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

			var stone = hand[step];
			stone.Position = DropPoint(hole, 0.32f);
			stone.Drop(new Vector3(0, -1.5f, 0));
			stone.Hole = hole;
			_inHole[hole].Add(stone);
			RefreshCounts();
		}
		await Wait(0.35f);
	}

	/// <summary>Where a carried gem floats: a loose little cluster above a hole.</summary>
	private static Vector3 HoverPoint(int hole, int index)
	{
		float angle = index * 2.4f;
		float radius = 0.05f + 0.025f * Mathf.Sqrt(index);
		return HoleCenter(hole) + new Vector3(Mathf.Cos(angle) * radius, 0.65f + index * 0.012f, Mathf.Sin(angle) * radius);
	}

	/// <summary>
	/// "The pit stop": the captured gems flare with light and fly to the store in a glowing arc,
	/// leaving a trail of sparks, then land with a rising run of clinks.
	/// </summary>
	private async Task CaptureStones(SowResult result)
	{
		int store = Kalah.StoreOf(result.Mover);
		int last = result.Path[^1];
		var taken = _inHole[result.CaptureFrom].Concat(_inHole[last]).ToList();
		Sparkle.Burst(this, HoleCenter(result.CaptureFrom) + new Vector3(0, 0.3f, 0));
		_inHole[result.CaptureFrom].Clear();
		_inHole[last].Clear();

		for (int i = 0; i < taken.Count; i++)
		{
			var stone = taken[i];
			stone.Hold();
			stone.Mesh.MaterialOverlay = Sparkle.Halo;
			var sparkle = new Sparkle();
			stone.AddChild(sparkle);

			var start = stone.Position;
			var end = DropPoint(store, 0.4f);
			var peak = (start + end) / 2 + new Vector3(0, 2.2f, 0);
			var flight = CreateTween();
			flight.TweenInterval(0.25f + i * 0.07f);
			flight.TweenMethod(Callable.From<float>(t =>
			{
				// A quadratic Bézier: up, over and down into the store.
				var a = start.Lerp(peak, t);
				var b = peak.Lerp(end, t);
				stone.Position = a.Lerp(b, t);
			}), 0f, 1f, 0.55f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			int index = i;
			flight.TweenCallback(Callable.From(() =>
			{
				stone.Hole = store;
				_inHole[store].Add(stone);
				stone.Drop(new Vector3(0, -1f, 0));
				sparkle.Finish();
				RefreshCounts();
				// Each landing a little louder, so a big capture rings up like a payout.
				_sound.PlayImpact(_clinks.Random(true), stone.GlobalPosition, 0.8f, 2f + index * 0.4f);
				GetTree().CreateTimer(0.6).Timeout += () => stone.Mesh.MaterialOverlay = null;
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

	/// <summary>Game over: the gems left on each side slide into their owner's store.</summary>
	private async Task SweepStones(IReadOnlyList<(int Hole, int Stones)> sweep)
	{
		if (sweep.Count == 0)
			return;
		await Wait(0.3f);
		int i = 0;
		foreach (var (hole, _) in sweep)
		{
			int store = Kalah.StoreOf(Kalah.OwnerOf(hole));
			foreach (var stone in _inHole[hole].ToList())
				Carry(stone, store, i++ * 0.05f);
			_inHole[hole].Clear();
		}
		RefreshCounts();
		await Wait(0.6f + i * 0.05f);
	}

	/// <summary>Lifts a gem and drops it into another hole (used to rearrange the board).</summary>
	private void Carry(Stone stone, int hole, float delay)
	{
		stone.Hold();
		stone.Hole = hole;
		if (!_inHole[hole].Contains(stone))
			_inHole[hole].Add(stone);
		var target = DropPoint(hole, 0.5f);
		var tween = CreateTween();
		tween.TweenInterval(delay);
		tween.TweenProperty(stone, "position", stone.Position with { Y = 0.8f }, 0.15f);
		tween.TweenProperty(stone, "position", target, 0.3f).SetTrans(Tween.TransitionType.Sine);
		tween.TweenCallback(Callable.From(() => stone.Drop(new Vector3(0, -1f, 0))));
	}

	/// <summary>
	/// Moves gems so the board matches the rules again, carrying each surplus gem to a hole that's
	/// short. Used for a new game and after Undo.
	/// </summary>
	private async Task MatchBoardToRules()
	{
		var spare = new List<Stone>();
		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			while (_inHole[hole].Count > _game[hole])
			{
				var stone = _inHole[hole][^1];
				_inHole[hole].RemoveAt(_inHole[hole].Count - 1);
				spare.Add(stone);
			}
		}
		int moved = 0;
		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			while (_inHole[hole].Count < _game[hole] && spare.Count > 0)
			{
				var stone = spare[^1];
				spare.RemoveAt(spare.Count - 1);
				Carry(stone, hole, moved++ * 0.03f);
			}
		}
		RefreshCounts();
		await Wait(0.5f + moved * 0.03f);
	}

	private void OnStoneLanded(Stone stone, float speed, Node other)
	{
		if (_quiet || speed < 0.6f)
			return;
		_sound.PlayImpact(_clinks.Random(other is Stone), stone.GlobalPosition, Mathf.Clamp(speed / 5f, 0.15f, 1f));
	}

	// ---------------------------------------------------------------- Computer

	public override void _Process(double delta) => UpdateComputer((float)delta);

	public override void _PhysicsProcess(double delta)
	{
		// Now and then, put back any gem that bounced out of its hole or off the board, so what you
		// see always matches the counts.
		_escapeCheck += (float)delta;
		if (_escapeCheck < 0.5f || _busy)
			return;
		_escapeCheck = 0f;
		foreach (var stones in _inHole)
		{
			foreach (var stone in stones)
			{
				if (stone.Freeze)
					continue;
				var offset = stone.Position - HoleCenter(stone.Hole);
				bool outside = new Vector2(offset.X, offset.Z).Length() > HoleReach(stone.Hole) + 0.12f || stone.Position.Y < -0.6f;
				if (!outside)
					continue;
				DevLog.Write($"stone escaped from hole {stone.Hole}, putting it back");
				stone.Hold();
				stone.Position = DropPoint(stone.Hole, 0.5f);
				stone.Drop(Vector3.Zero);
			}
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
	/// (the computer plays both sides), `--quit-after-demo`, `--games-since-ad=4`.
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
			}
		}
	}
}
