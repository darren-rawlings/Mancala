using Godot;
using Renegade.Core.Synth;

namespace Mancala.Game;

/// <summary>A material family. Each has a look, a sound, and how bouncy it is. Same set as Counter Fire.</summary>
public enum Style
{
	Plastic,
	Wood,
	Metal,
	Glass,
	Marble,

	/// <summary>The tabletop under the board. Not selectable in the menu.</summary>
	Table,
}

/// <summary>Shader settings for one surface. <see cref="Pattern"/> ids match surface.gdshader.</summary>
public sealed record Look(
	int Pattern,
	Color A,
	Color B,
	float Roughness,
	float Metallic = 0f,
	float Scale = 1f,
	float Opacity = 1f)
{
	public const int PlasticPattern = 0;
	public const int WoodPattern = 1;
	public const int MetalPattern = 2;
	public const int MarblePattern = 3;
	public const int GlassPattern = 4;
	public const int PlankPattern = 5;

	public bool IsGlass => Pattern == GlassPattern;
}

/// <summary>Every tunable number for the materials lives here, so restyling never touches game code.</summary>
public static class Styles
{
	public static readonly Style[] Choices = { Style.Plastic, Style.Wood, Style.Metal, Style.Glass, Style.Marble };

	public static string BoardName(Style style) => style switch
	{
		Style.Plastic => "Classic",
		Style.Wood => "Oak",
		Style.Metal => "Steel",
		Style.Glass => "Glass",
		Style.Marble => "Marble",
		_ => "Table",
	};

	public static string BeadName(Style style) => style switch
	{
		Style.Plastic => "Red",
		Style.Wood => "Walnut",
		Style.Metal => "Silver",
		Style.Glass => "Sapphire",
		Style.Marble => "Ivory",
		_ => "",
	};

	public static Look BoardLook(Style style) => style switch
	{
		Style.Wood => new(Look.WoodPattern, new("a4693c"), new("845030"), 0.5f, Scale: 1.1f),
		Style.Metal => new(Look.MetalPattern, new("c3c9d2"), new("7d848f"), 0.3f, Metallic: 1f),
		Style.Glass => new(Look.GlassPattern, new("9fd4ff"), new("5aa0e0"), 0.05f, Opacity: 0.35f),
		Style.Marble => new(Look.MarblePattern, new("b9b3a8"), new("5e5a54"), 0.2f, Scale: 0.55f),
		Style.Table => new(Look.PlankPattern, new("4a2e1c"), new("2c1a10"), 0.75f, Scale: 0.6f),
		_ => new(Look.PlasticPattern, new("2f5fe8"), new("1c3a9c"), 0.3f),
	};

	/// <summary>Every marble in a game shares one look, set by the bead material.</summary>
	public static Look BeadLook(Style style) => style switch
	{
		Style.Wood => new(Look.WoodPattern, new("6b4426"), new("3b2414"), 0.55f, Scale: 3f),
		Style.Metal => new(Look.MetalPattern, new("e3e7ec"), new("9aa1ab"), 0.22f, Metallic: 1f),
		Style.Glass => new(Look.GlassPattern, new("2563eb"), new("93c5fd"), 0.04f, Opacity: 0.62f),
		Style.Marble => new(Look.MarblePattern, new("f3eee2"), new("b9ae96"), 0.15f, Scale: 4f),
		_ => new(Look.PlasticPattern, new("ef4444"), new("b91c1c"), 0.28f),
	};

	public static ImpactRecipe Impact(Style style) => style switch
	{
		Style.Wood => new(750, new[] { 1.0, 2.57, 4.2, 6.1 }, new[] { 1.0, 0.6, 0.3, 0.15 }, 0.045, 0.7, 0.006, 0.35, 0.3),
		Style.Metal => new(2100, new[] { 1.0, 2.756, 5.404, 8.933 }, new[] { 1.0, 0.7, 0.5, 0.3 }, 0.45, 0.3, 0.002, 0.7, 0.9),
		// Tuned in Counter Fire to carry on phone speakers: a lower ring with a sharp clink.
		Style.Glass => new(2300, new[] { 1.0, 2.32, 4.25, 6.63 }, new[] { 1.0, 0.6, 0.45, 0.3 }, 0.18, 0.9, 0.004, 0.8, 0.6),
		Style.Marble => new(1400, new[] { 1.0, 1.83, 2.9, 4.4 }, new[] { 1.0, 0.7, 0.4, 0.2 }, 0.05, 0.9, 0.005, 0.55, 0.3),
		Style.Table => new(420, new[] { 1.0, 2.4, 3.9 }, new[] { 1.0, 0.4, 0.2 }, 0.03, 0.8, 0.008, 0.2, 0.25),
		_ => new(1900, new[] { 1.0, 2.2, 3.7 }, new[] { 1.0, 0.5, 0.25 }, 0.05, 0.5, 0.004, 0.5, 0.3),
	};

	/// <summary>Long-ringing materials are quieter per hit so a cascade doesn't get harsh.</summary>
	public static float GainDb(Style style) => style switch
	{
		Style.Metal => -5f,
		Style.Glass => -2f,
		Style.Table => -2f,
		_ => 0f,
	};

	public static float Bounce(Style style) => style switch
	{
		Style.Metal => 0.3f,
		Style.Glass => 0.3f,
		Style.Wood => 0.15f,
		Style.Marble => 0.15f,
		_ => 0.2f,
	};
}
