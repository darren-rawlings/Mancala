// Opaque material for boards, discs and the table. One shader, several procedural patterns.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/noise.gdshaderinc"

// 0 plastic, 1 wood, 2 brushed metal, 3 marble, 5 planks, 6 tatami. Must match Look in Styles.cs.
uniform int pattern = 0;
uniform vec3 color_a : source_color = vec3(0.18, 0.37, 0.91);
uniform vec3 color_b : source_color = vec3(0.11, 0.23, 0.61);
uniform float roughness_base : hint_range(0.0, 1.0) = 0.3;
uniform float metallic_base : hint_range(0.0, 1.0) = 0.0;
uniform float pattern_scale = 1.0;
uniform vec3 seed = vec3(0.0);
// Discs are lathed around their local Y axis; this turns on the moulded rings and radial brushing.
uniform bool is_disc = false;

varying vec3 obj;

void vertex() {
	obj = VERTEX;
}

void fragment() {
	vec3 p = obj * pattern_scale + seed;
	vec3 col = color_a;
	float rough = roughness_base;
	float radius = length(obj.xz);

	if (pattern == 1) {
		// Wood: growth rings warped by noise, plus fine fibres along the grain.
		float warp = fbm(p * vec3(0.8, 0.25, 0.8)) * 2.2;
		float rings = fract(length(p.xz + vec2(4.3, 2.1)) * 2.6 + warp);
		float late = smoothstep(0.55, 0.95, rings);
		float fibre = vnoise(p * vec3(26.0, 1.2, 26.0));
		col = mix(color_a, color_b, clamp(late * 0.85 + fibre * 0.25, 0.0, 1.0));
		rough = roughness_base + late * 0.12;
	} else if (pattern == 2) {
		// Brushed metal: fine streaks, straight on the board and circular (lathe-turned) on discs.
		float across = is_disc ? radius * 90.0 : p.y * 90.0;
		float along = is_disc ? atan(obj.z, obj.x) * 2.0 : p.x * 0.6;
		float streak = vnoise(vec3(across, along, seed.x));
		col = mix(color_b, color_a, 0.55 + 0.45 * streak);
		rough = roughness_base + (streak - 0.5) * 0.18;
	} else if (pattern == 3) {
		// Marble: soft cloudy body with fine, slightly blurred veins winding through it.
		float n = fbm(p * 1.4);
		float v = abs(sin((p.x * 1.3 + p.y * 0.7 + p.z * 0.9 + n * 5.0) * 1.7));
		float vein = pow(1.0 - v, 12.0) * 0.9 + pow(1.0 - v, 4.0) * 0.25;
		float cloud = fbm(p * 3.1 + vec3(5.2));
		col = mix(color_a, color_b, clamp(vein + (cloud - 0.5) * 0.25, 0.0, 1.0));
	} else if (pattern == 5) {
		// Planks (tables): grain runs along X, boards are separated by dark seams along Z,
		// and each board gets its own shade and grain offset.
		float board = floor(p.z * 1.1);
		float along = fract(p.z * 1.1);
		float shade = hash13(vec3(board, 3.7, 1.9));
		float grain = fract(p.z * 9.0 + fbm(vec3(p.x * 0.35, board * 7.0, 0.0)) * 3.0);
		float fibre = vnoise(vec3(p.x * 1.5, p.z * 60.0, board));
		col = mix(color_a, color_b, clamp(smoothstep(0.6, 0.95, grain) * 0.7 + fibre * 0.25 + shade * 0.35, 0.0, 1.0));
		col *= mix(0.45, 1.0, smoothstep(0.0, 0.025, along) * smoothstep(1.0, 0.975, along));
	} else if (pattern == 6) {
		// Tatami: tightly woven rush strands with a dark cloth border along each mat's long edge.
		float strand = smoothstep(0.2, 0.5, abs(fract(p.z * 12.0) - 0.5));
		float weave = vnoise(vec3(p.x * 4.0, p.z * 12.0, 0.0));
		col = mix(color_a, color_b, clamp(strand * 0.7 + weave * 0.3, 0.0, 1.0));
		float mat = fract(p.z * 0.33);
		col = mix(col, vec3(0.13, 0.11, 0.09), step(mat, 0.045));
	} else {
		// Plastic: almost flat, with a faint speckle so it doesn't look like a render error.
		col *= 0.97 + 0.03 * vnoise(p * 30.0);
	}

	if (is_disc) {
		// Concentric moulding rings in the recessed face, like a real Connect Four checker.
		float ring = smoothstep(0.35, 0.5, abs(fract(radius * 22.0) - 0.5))
			* step(radius, 0.30) * step(0.06, abs(obj.y));
		col *= 1.0 - 0.10 * ring;
	}

	ALBEDO = col;
	ROUGHNESS = clamp(rough, 0.02, 1.0);
	METALLIC = metallic_base;
}
