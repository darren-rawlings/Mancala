using Godot;
using Mancala.Core;

namespace Mancala.Game;

/// <summary>
/// Where everything is, in board space (y up, the board's top surface at y = 0). The board lies
/// along Z so it fills a portrait phone. Holes run counter-clockwise seen from above:
/// side One's pits up the right column (nearest the camera first), One's store at the far end,
/// side Two's pits down the left column, Two's store at the near end.
/// </summary>
public static class BoardLayout
{
	public const float PitPitch = 1.06f;
	public const float PitRadius = 0.46f;
	public const float PitDepth = 0.36f;
	public const float ColumnX = 0.58f;

	public const float StoreHalfWidth = 0.52f;
	public const float StoreHalfLength = 1.05f;
	public const float StoreDepth = 0.5f;
	public const float StoreZ = 3f * PitPitch + 0.12f + StoreHalfLength;

	public const float HalfWidth = 1.24f;
	public const float HalfLength = StoreZ + StoreHalfLength + 0.25f;
	public const float Thickness = 0.7f;

	public const float StoneRadius = 0.13f;
	public const float StoneHeight = 0.15f;

	/// <summary>Centre of a hole, on the board's top surface.</summary>
	public static Vector3 HoleCenter(int hole) => hole switch
	{
		Kalah.StoreOne => new Vector3(0, 0, -StoreZ),
		Kalah.StoreTwo => new Vector3(0, 0, StoreZ),
		< Kalah.StoreOne => new Vector3(ColumnX, 0, (2.5f - hole) * PitPitch),
		_ => new Vector3(-ColumnX, 0, (hole - 7 - 2.5f) * PitPitch),
	};

	/// <summary>How far a stone may sit from a hole's centre and still be "in" it.</summary>
	public static float HoleReach(int hole) => Kalah.IsStore(hole) ? StoreHalfLength : PitRadius;

	/// <summary>
	/// Height of the carved top surface: flat wood, with a smooth round bowl for each pit and a long
	/// oval trough for each store. A bowl is a paraboloid with its rim eased so the edge isn't sharp.
	/// </summary>
	public static float SurfaceHeight(float x, float z)
	{
		float height = 0f;
		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			var c = HoleCenter(hole);
			bool store = Kalah.IsStore(hole);
			float a = store ? StoreHalfWidth : PitRadius;
			float b = store ? StoreHalfLength : PitRadius;
			float dx = (x - c.X) / a, dz = (z - c.Z) / b;
			float d2 = dx * dx + dz * dz;
			if (d2 >= 1f)
				continue;
			float bowl = 1f - d2;
			// Soften the rim: the slope eases in over the outer 15% instead of starting abruptly.
			float rim = Mathf.SmoothStep(0f, 0.28f, bowl);
			height = Mathf.Min(height, -(store ? StoreDepth : PitDepth) * bowl * rim);
		}
		return height;
	}
}
