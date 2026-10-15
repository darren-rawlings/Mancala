using System;
using Godot;
using Mancala.Core;

namespace Mancala.Game.Ui;

/// <summary>
/// Title, score, turn status and buttons. Everything that isn't a button ignores the mouse so
/// taps reach the 3D board underneath.
/// </summary>
public partial class Hud : CanvasLayer
{
	public event Action? NewGamePressed;
	public event Action? UndoPressed;
	public event Action<bool>? MuteToggled;
	public event Action<GameMode, Difficulty>? ModeChosen;
	public event Action<Style>? BoardChosen;
	public event Action<Style>? BeadsChosen;

	private Label _score = null!;
	private Label _status = null!;
	private Button _newGame = null!;
	private Button _undo = null!;
	private Button _mute = null!;
	private Button _pvp = null!;
	private Button _pvc = null!;
	private Control _difficultyPopup = null!;
	private Control _menu = null!;
	private Label _hint = null!;

	public void Build(string title, Settings settings)
	{
		bool muted = settings.Muted;
		var heading = MakeLabel(title.ToUpperInvariant(), 56, Palette.Text);
		Anchor(heading, Control.LayoutPreset.TopWide, top: 40, bottom: 110);
		AddChild(heading);

		_score = MakeLabel("", 28, Palette.TextMuted);
		Anchor(_score, Control.LayoutPreset.TopWide, top: 108, bottom: 150);
		AddChild(_score);

		_status = MakeLabel("", 38, Palette.Text);
		Anchor(_status, Control.LayoutPreset.BottomWide, top: -250, bottom: -196);
		AddChild(_status);

		_newGame = MakeButton("New Game", 30, Palette.Accent);
		Anchor(_newGame, Control.LayoutPreset.CenterBottom, left: -150, right: 150, top: -192, bottom: -128);
		_newGame.Pressed += () => NewGamePressed?.Invoke();
		_newGame.Visible = false;
		AddChild(_newGame);

		// Undo is only offered against the computer, and it plays a rewarded ad, so the label says so.
		_undo = MakeButton("Undo (ad)", 22, Palette.ButtonDark);
		Anchor(_undo, Control.LayoutPreset.BottomLeft, left: 20, right: 168, top: -186, bottom: -130);
		_undo.Pressed += () => UndoPressed?.Invoke();
		_undo.Visible = false;
		AddChild(_undo);

		_mute = MakeButton("", 22, Palette.ButtonDark);
		Anchor(_mute, Control.LayoutPreset.BottomRight, left: -168, right: -20, top: -186, bottom: -130);
		_mute.Pressed += () =>
		{
			muted = !muted;
			SetMuteText(muted);
			MuteToggled?.Invoke(muted);
		};
		SetMuteText(muted);
		AddChild(_mute);

		_hint = MakeLabel("Tap a glowing pit  ·  Drag sideways to spin", 24, Palette.TextMuted);
		Anchor(_hint, Control.LayoutPreset.BottomWide, top: -290, bottom: -254);
		AddChild(_hint);

		var style = MakeButton("Style", 24, Palette.ButtonDark);
		Anchor(style, Control.LayoutPreset.TopLeft, left: 20, right: 140, top: 160, bottom: 212);
		style.Pressed += () => _menu.Visible = true;
		AddChild(style);

		BuildModeButtons(settings.Mode, settings.Difficulty);

		_menu = BuildMenu(settings.Board, settings.Beads);
		_menu.Visible = false;
		AddChild(_menu);
	}

	/// <summary>True while a menu or pop-up is open; the board ignores taps meanwhile.</summary>
	public bool MenuOpen => _difficultyPopup.Visible || _menu.Visible;

	/// <summary>The controls hint fades after the first move; it's only needed once.</summary>
	public void HideHint()
	{
		if (_hint.Modulate.A < 1f)
			return;
		CreateTween().TweenProperty(_hint, "modulate:a", 0f, 0.6f);
	}

	public void SetStatus(string text, Color color)
	{
		_status.Text = text;
		_status.AddThemeColorOverride("font_color", color);
	}

	public void SetScore(string text) => _score.Text = text;

	public void SetNewGameVisible(bool visible) => _newGame.Visible = visible;

	public void SetUndoVisible(bool visible) => _undo.Visible = visible;

	/// <summary>
	/// Two buttons side by side at the bottom. Choosing "vs Computer" pops up the difficulty choice
	/// first; the mode only changes once a level is picked, and tapping outside leaves it as it was.
	/// </summary>
	private void BuildModeButtons(GameMode mode, Difficulty difficulty)
	{
		_pvp = MakeButton("2 Players", 24, Palette.ButtonDark);
		Anchor(_pvp, Control.LayoutPreset.CenterBottom, left: -330, right: -10, top: -110, bottom: -46);
		AddChild(_pvp);

		_pvc = MakeButton("", 24, Palette.ButtonDark);
		Anchor(_pvc, Control.LayoutPreset.CenterBottom, left: 10, right: 330, top: -110, bottom: -46);
		AddChild(_pvc);

		_pvp.Pressed += () => ChooseMode(GameMode.PlayerVsPlayer, difficulty);
		_pvc.Pressed += () => _difficultyPopup.Visible = true;

		// Transparent full-screen catcher: a tap anywhere outside the three buttons closes it.
		var catcher = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
		catcher.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		catcher.GuiInput += input =>
		{
			if (input is InputEventMouseButton { Pressed: true })
				_difficultyPopup.Visible = false;
		};
		_difficultyPopup = catcher;

		var panel = new PanelContainer();
		var box = new StyleBoxFlat { BgColor = Palette.Panel, AntiAliasing = true };
		box.SetCornerRadiusAll(28);
		box.SetContentMarginAll(14);
		panel.AddThemeStyleboxOverride("panel", box);
		Anchor(panel, Control.LayoutPreset.CenterBottom, left: -230, right: 230, top: -200, bottom: -118);
		catcher.AddChild(panel);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		panel.AddChild(row);
		foreach (var level in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
		{
			var button = MakeButton(level.ToString(), 26, Palette.Accent);
			button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			button.Pressed += () =>
			{
				difficulty = level;
				_difficultyPopup.Visible = false;
				ChooseMode(GameMode.PlayerVsComputer, level);
			};
			row.AddChild(button);
		}
		catcher.Visible = false;
		AddChild(catcher);

		ShowMode(mode, difficulty);
	}

	private void ChooseMode(GameMode mode, Difficulty difficulty)
	{
		ShowMode(mode, difficulty);
		ModeChosen?.Invoke(mode, difficulty);
	}

	/// <summary>Highlights the active mode; the computer button names its level.</summary>
	private void ShowMode(GameMode mode, Difficulty difficulty)
	{
		bool vsComputer = mode == GameMode.PlayerVsComputer;
		_pvc.Text = vsComputer ? $"vs Computer: {difficulty}" : "vs Computer";
		Restyle(_pvp, vsComputer ? Palette.ButtonDark : Palette.Accent);
		Restyle(_pvc, vsComputer ? Palette.Accent : Palette.ButtonDark);
	}

	private Control BuildMenu(Style board, Style beads)
	{
		// A light scrim blocks taps on the board while still showing style changes live behind it.
		var scrim = new ColorRect { Color = new Color(0, 0, 0, 0.25f), MouseFilter = Control.MouseFilterEnum.Stop };
		scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);

		var panel = new PanelContainer();
		var box = new StyleBoxFlat { BgColor = Palette.Panel, AntiAliasing = true };
		box.SetCornerRadiusAll(28);
		box.SetContentMarginAll(28);
		panel.AddThemeStyleboxOverride("panel", box);
		Anchor(panel, Control.LayoutPreset.BottomWide, left: 24, right: -24, top: -560, bottom: -24);
		scrim.AddChild(panel);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 16);
		panel.AddChild(column);

		column.AddChild(MakeLabel("Board", 30, Palette.TextMuted));
		column.AddChild(MakeChoices(Styles.Choices, board, Styles.BoardName, s => BoardChosen?.Invoke(s)));
		column.AddChild(MakeLabel("Marbles", 30, Palette.TextMuted));
		column.AddChild(MakeChoices(Styles.Choices, beads, Styles.BeadName, s => BeadsChosen?.Invoke(s)));

		var done = MakeButton("Done", 30, Palette.Accent);
		done.CustomMinimumSize = new Vector2(0, 70);
		done.Pressed += () => _menu.Visible = false;
		column.AddChild(done);
		return scrim;
	}

	private static GridContainer MakeChoices<T>(T[] choices, T selected, Func<T, string> name, Action<T> chosen)
		where T : struct, Enum
	{
		var grid = new GridContainer { Columns = 3 };
		grid.AddThemeConstantOverride("h_separation", 12);
		grid.AddThemeConstantOverride("v_separation", 12);
		var group = new ButtonGroup();
		foreach (var choice in choices)
		{
			var button = MakeButton(name(choice), 26, Palette.ButtonDark);
			button.ToggleMode = true;
			button.ButtonGroup = group;
			button.ButtonPressed = choice.Equals(selected);
			button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			button.CustomMinimumSize = new Vector2(0, 64);
			button.Pressed += () => chosen(choice);
			grid.AddChild(button);
		}
		return grid;
	}

	private void SetMuteText(bool muted) => _mute.Text = muted ? "Sound: Off" : "Sound: On";

	private static Label MakeLabel(string text, int fontSize, Color color)
	{
		var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
		label.AddThemeConstantOverride("shadow_offset_y", 3);
		// A soft outline keeps text readable over the board and the table.
		label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.55f));
		label.AddThemeConstantOverride("outline_size", 8);
		return label;
	}

	private static Button MakeButton(string text, int fontSize, Color color)
	{
		var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
		button.AddThemeFontSizeOverride("font_size", fontSize);
		Restyle(button, color);
		return button;
	}

	private static void Restyle(Button button, Color color)
	{
		foreach (var (state, shade) in new[] { ("normal", color), ("hover", color.Lightened(0.12f)), ("pressed", Palette.Accent), ("hover_pressed", Palette.Accent.Lightened(0.12f)) })
		{
			var box = new StyleBoxFlat { BgColor = shade, AntiAliasing = true };
			box.SetCornerRadiusAll(32);
			box.SetContentMarginAll(12);
			button.AddThemeStyleboxOverride(state, box);
		}
	}

	private static void Anchor(Control control, Control.LayoutPreset preset, float left = 0, float right = 0, float top = 0, float bottom = 0)
	{
		control.SetAnchorsPreset(preset);
		control.OffsetLeft = left;
		control.OffsetRight = right;
		control.OffsetTop = top;
		control.OffsetBottom = bottom;
	}
}
