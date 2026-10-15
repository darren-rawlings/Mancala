using Godot;
using Mancala.Core;

namespace Mancala.Game;

/// <summary>
/// Where everything is, in board space (y up, the board's top surface at y = 0). The board lies
/// along X, the traditional way round: your row (side One) nearest you, sowing left to right into
/// your store on the right; side Two's row behind, running right to left into their store on the
/// left. That's counter-clockwise seen from above.
/// </summary>
public static class BoardLayout
{
	public const float PitPitch = 1.06f;
	public const float PitRadius = 0.46f;
	public const float PitDepth = 0.44f;
	public const float RowZ = 0.6f;

	/// <summary>Stores are rounded rectangles running across the board, spanning both rows.</summary>
	public const float StoreHalfX = 0.52f;
	public const float StoreHalfZ = 1.06f;
	public const float StoreCorner = 0.36f;
	public const float StoreDepth = 0.55f;
	public const float StoreX = 3f * PitPitch + 0.12f + StoreHalfX;

	public const float HalfX = StoreX + StoreHalfX + 0.24f;
	public const float HalfZ = RowZ + PitRadius + 0.24f;
	public const float Thickness = 0.8f;

	public const float BeadRadius = 0.12f;

	/// <summary>Centre of a hole, on the board's top surface.</summary>
	public static Vector3 HoleCenter(int hole) => hole switch
	{
		Kalah.StoreOne => new Vector3(StoreX, 0, 0),
		Kalah.StoreTwo => new Vector3(-StoreX, 0, 0),
		< Kalah.StoreOne => new Vector3((hole - 2.5f) * PitPitch, 0, RowZ),
		_ => new Vector3((2.5f - (hole - 7)) * PitPitch, 0, -RowZ),
	};

	/// <summary>
	/// Signed distance from a point (on the board, y ignored) to a hole's rim: negative inside.
	/// Pits are circles; stores are rounded rectangles.
	/// </summary>
	public static float DistanceToRim(int hole, Vector3 point)
	{
		var c = HoleCenter(hole);
		float dx = point.X - c.X, dz = point.Z - c.Z;
		if (!Kalah.IsStore(hole))
			return Mathf.Sqrt(dx * dx + dz * dz) - PitRadius;
		float qx = Mathf.Abs(dx) - (StoreHalfX - StoreCorner);
		float qz = Mathf.Abs(dz) - (StoreHalfZ - StoreCorner);
		float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qz, 0)).Length();
		return outside + Mathf.Min(Mathf.Max(qx, qz), 0) - StoreCorner;
	}

	/// <summary>
	/// Height of the carved top surface: flat wood, a round bowl for each pit and a rounded-rectangle
	/// trough for each store. Pits are paraboloids with an eased rim; stores have sloped walls and a
	/// flat floor, so marbles spread out across them instead of piling in one spot.
	/// </summary>
	public static float SurfaceHeight(float x, float z)
	{
		float height = 0f;
		var p = new Vector3(x, 0, z);
		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			float d = DistanceToRim(hole, p);
			if (d >= 0f)
				continue;
			if (Kalah.IsStore(hole))
			{
				float t = Mathf.Clamp(-d / 0.35f, 0f, 1f);
				height = Mathf.Min(height, -StoreDepth * Mathf.SmoothStep(0f, 1f, t));
			}
			else
			{
				float r = (d + PitRadius) / PitRadius;
				float bowl = 1f - r * r;
				height = Mathf.Min(height, -PitDepth * bowl * Mathf.SmoothStep(0f, 0.28f, bowl));
			}
		}
		return height;
	}
}
