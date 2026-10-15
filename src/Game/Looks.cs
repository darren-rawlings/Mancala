using Godot;
using Renegade.Core.Synth;

namespace Mancala.Game;

/// <summary>Materials, gem colours and sounds: every tunable number for the look and sound lives here.</summary>
public static class Looks
{
	private static readonly Shader SurfaceShader = GD.Load<Shader>("res://shaders/surface.gdshader");
	private static readonly Shader GlassShader = GD.Load<Shader>("res://shaders/glass.gdshader");

	// Pattern ids from surface.gdshader.
	private const int Wood = 1;
	private const int Planks = 5;

	public static ShaderMaterial BoardWood { get; } = Surface(Wood, new("a4693c"), new("845030"), 0.5f, 1.1f);

	public static ShaderMaterial Table { get; } = Surface(Planks, new("4a2e1c"), new("2c1a10"), 0.75f, 0.6f);

	/// <summary>Traditional mixed glass gems: each stone gets one of these at random.</summary>
	public static readonly Color[] GemColors =
	{
		new("3fa2ff"), new("33d17a"), new("ff5a5a"), new("ffcc33"), new("b36bff"), new("39e0d0"), new("ff8a3d"),
	};

	private static ShaderMaterial[]? _gems;

	public static ShaderMaterial Gem(int index)
	{
		_gems ??= System.Array.ConvertAll(GemColors, c =>
		{
			var material = new ShaderMaterial { Shader = GlassShader };
			material.SetShaderParameter("color_a", c);
			material.SetShaderParameter("color_b", c.Lightened(0.45f));
			material.SetShaderParameter("opacity", 0.72f);
			material.SetShaderParameter("roughness_base", 0.06f);
			material.SetShaderParameter("is_disc", true);
			return material;
		});
		return _gems[index % _gems.Length];
	}

	/// <summary>Glass gem on glass gem: the bright clink retuned in Counter Fire to carry on phone speakers.</summary>
	public static readonly ImpactRecipe GemOnGem = new(2300, new[] { 1.0, 2.32, 4.25, 6.63 }, new[] { 1.0, 0.6, 0.45, 0.3 }, 0.18, 0.9, 0.004, 0.8, 0.6);

	/// <summary>A gem dropping into a bare wooden pit: a short, warm tock.</summary>
	public static readonly ImpactRecipe GemOnWood = new(900, new[] { 1.0, 2.57, 4.2, 6.1 }, new[] { 1.0, 0.6, 0.3, 0.15 }, 0.045, 0.7, 0.005, 0.45, 0.3);

	private static ShaderMaterial Surface(int pattern, Color a, Color b, float roughness, float scale)
	{
		var material = new ShaderMaterial { Shader = SurfaceShader };
		material.SetShaderParameter("pattern", pattern);
		material.SetShaderParameter("color_a", a);
		material.SetShaderParameter("color_b", b);
		material.SetShaderParameter("roughness_base", roughness);
		material.SetShaderParameter("pattern_scale", scale);
		return material;
	}
}
