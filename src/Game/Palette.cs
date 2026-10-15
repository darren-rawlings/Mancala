using Godot;

namespace Mancala.Game;

/// <summary>Interface colours: warm, to sit with the wood and the glowing gems.</summary>
public static class Palette
{
	public static readonly Color Text = new("fff3e0");
	public static readonly Color TextMuted = new("d9b99b");
	public static readonly Color Accent = new("e0782a");
	public static readonly Color ButtonDark = new("4a2c1a");
	public static readonly Color Panel = new Color("2a180e") with { A = 0.94f };

	/// <summary>Each side's colour in the status line and score.</summary>
	public static readonly Color SideOne = new("ffcc66");
	public static readonly Color SideTwo = new("8fd3ff");
}
