// Transparent material for glass boards and discs. Kept separate from surface.gdshader because
// writing ALPHA moves every user of a shader into the slower, unsorted transparent pass.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, specular_schlick_ggx;

#include "res://shaders/noise.gdshaderinc"

uniform vec3 color_a : source_color = vec3(0.62, 0.83, 1.0);
uniform vec3 color_b : source_color = vec3(0.35, 0.63, 0.88);
uniform float opacity : hint_range(0.0, 1.0) = 0.4;
uniform float roughness_base : hint_range(0.0, 1.0) = 0.05;
uniform float pattern_scale = 1.0;
uniform vec3 seed = vec3(0.0);
uniform bool is_disc = false;

varying vec3 obj;

void vertex() {
	obj = VERTEX;
}

void fragment() {
	// Glass gets denser and more reflective at grazing angles (Fresnel), which sells the edges.
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	vec3 p = obj * pattern_scale + seed;
	// Tiny trapped bubbles, only in the discs; on a whole board they read as dust.
	float bubble = is_disc ? smoothstep(0.93, 0.95, vnoise(p * 7.0)) : 0.0;

	ALBEDO = mix(color_a, color_b, fresnel) + bubble * 0.25;
	ALPHA = clamp(opacity + fresnel * 0.45 + bubble * 0.3, 0.0, 1.0);
	ROUGHNESS = roughness_base;
	METALLIC = 0.0;
	SPECULAR = 0.9;
}
