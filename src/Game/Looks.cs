using System.Collections.Generic;
using Godot;

namespace Mancala.Game;

/// <summary>Turns <see cref="Look"/> records into shader materials, one cached per look.</summary>
public static class Looks
{
	private static readonly Shader SurfaceShader = GD.Load<Shader>("res://shaders/surface.gdshader");
	private static readonly Shader GlassShader = GD.Load<Shader>("res://shaders/glass.gdshader");
	private static readonly Dictionary<Look, ShaderMaterial> Cache = new();

	public static ShaderMaterial For(Look look)
	{
		if (Cache.TryGetValue(look, out var cached))
			return cached;
		var material = new ShaderMaterial { Shader = look.IsGlass ? GlassShader : SurfaceShader };
		material.SetShaderParameter("color_a", look.A);
		material.SetShaderParameter("color_b", look.B);
		material.SetShaderParameter("roughness_base", look.Roughness);
		material.SetShaderParameter("pattern_scale", look.Scale);
		if (look.IsGlass)
		{
			material.SetShaderParameter("opacity", look.Opacity);
		}
		else
		{
			material.SetShaderParameter("pattern", look.Pattern);
			material.SetShaderParameter("metallic_base", look.Metallic);
		}
		Cache[look] = material;
		return material;
	}
}
