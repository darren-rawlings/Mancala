using Godot;
using Mancala.Core;
using static Mancala.Game.BoardLayout;

namespace Mancala.Game;

/// <summary>
/// The carved wooden board on its table: a smooth surface generated from
/// <see cref="BoardLayout.SurfaceHeight"/> (the same heights drive the collision, so gems settle
/// into exactly the bowls you see), a count above every hole, and rings that light up the pits
/// you can play.
/// </summary>
public partial class Board : Node3D
{
	/// <summary>Visual mesh resolution. Fine enough that the bowls look round, not faceted.</summary>
	private const float MeshCell = 0.04f;

	/// <summary>Collision resolution. Coarser than the mesh: gems only need the bowls' shape.</summary>
	private const float CollisionCell = 0.05f;

	private readonly Label3D[] _counts = new Label3D[Kalah.Holes];
	private readonly MeshInstance3D[] _rings = new MeshInstance3D[Kalah.Holes];
	private readonly StandardMaterial3D _ringMaterial = new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		AlbedoColor = new Color(1f, 0.78f, 0.35f, 0.75f),
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
	};

	public void Build()
	{
		AddChild(new MeshInstance3D { Mesh = TopMesh(), MaterialOverride = Looks.BoardWood });
		// The body: a slab whose top sits below the deepest bowl (a slab right under the carved top
		// hid every bowl), with thin walls round the edge, stopping a hair under the top so the
		// two never z-fight.
		float under = StoreDepth + 0.03f;
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(HalfWidth * 2, Thickness - under, HalfLength * 2) },
			MaterialOverride = Looks.BoardWood,
			Position = new Vector3(0, -under - (Thickness - under) / 2, 0),
		});
		const float wall = 0.02f;
		foreach (var (size, offset) in new[]
		{
			(new Vector3(HalfWidth * 2, under, wall), new Vector3(0, 0, HalfLength - wall / 2)),
			(new Vector3(HalfWidth * 2, under, wall), new Vector3(0, 0, -HalfLength + wall / 2)),
			(new Vector3(wall, under, HalfLength * 2), new Vector3(HalfWidth - wall / 2, 0, 0)),
			(new Vector3(wall, under, HalfLength * 2), new Vector3(-HalfWidth + wall / 2, 0, 0)),
		})
		{
			AddChild(new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = size },
				MaterialOverride = Looks.BoardWood,
				Position = offset + new Vector3(0, -under / 2 - 0.003f, 0),
			});
		}

		var body = new StaticBody3D();
		body.AddChild(new CollisionShape3D { Shape = TopCollision(), Scale = Vector3.One * CollisionCell });
		body.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(HalfWidth * 2, Thickness, HalfLength * 2) },
			Position = new Vector3(0, -Thickness / 2 - 0.02f, 0),
		});
		AddChild(body);

		var table = new StaticBody3D { Position = new Vector3(0, -Thickness, 0) };
		table.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(30, 30) }, MaterialOverride = Looks.Table });
		table.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
		AddChild(table);

		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			bool store = Kalah.IsStore(hole);
			var center = HoleCenter(hole);
			// Counts sit just outside each hole, on the side nearer the middle of the board for
			// pits, and past the end for stores, so piles of gems never hide them.
			var offset = store
				? new Vector3(HalfWidth + 0.3f, 0.05f, 0)
				: new Vector3(Mathf.Sign(center.X) * (PitRadius + 0.13f), 0.05f, 0);
			_counts[hole] = new Label3D
			{
				Position = center + offset,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FontSize = store ? 96 : 64,
				PixelSize = 0.0035f,
				OutlineSize = 18,
				Modulate = new Color("fff3e0"),
				OutlineModulate = new Color(0, 0, 0, 0.7f),
				NoDepthTest = true,
			};
			AddChild(_counts[hole]);

			if (!store)
			{
				_rings[hole] = new MeshInstance3D
				{
					Mesh = new TorusMesh { InnerRadius = PitRadius - 0.025f, OuterRadius = PitRadius + 0.02f, Rings = 48, RingSegments = 8 },
					MaterialOverride = _ringMaterial,
					Position = center + new Vector3(0, 0.005f, 0),
					Scale = new Vector3(1, 0.15f, 1),
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
					Visible = false,
				};
				AddChild(_rings[hole]);
			}
		}
	}

	public void SetCount(int hole, int count) => _counts[hole].Text = count.ToString();

	/// <summary>Lights the rings round the pits that can be played now; pass nothing to turn them all off.</summary>
	public void HighlightPits(System.Collections.Generic.IEnumerable<int>? pits)
	{
		foreach (var ring in _rings)
		{
			if (ring != null)
				ring.Visible = false;
		}
		if (pits == null)
			return;
		foreach (int pit in pits)
			_rings[pit].Visible = true;
	}

	public override void _Process(double delta)
	{
		// A slow breathe on the playable rings, so they read as "tap me" without flashing.
		float t = Time.GetTicksMsec() / 1000f;
		_ringMaterial.AlbedoColor = _ringMaterial.AlbedoColor with { A = 0.45f + 0.3f * (0.5f + 0.5f * Mathf.Sin(t * 3f)) };
	}

	/// <summary>Pit nearest a point on the board, if the point is inside it.</summary>
	public static int PitAt(Vector3 point)
	{
		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			if (!Kalah.IsStore(hole) && (HoleCenter(hole) - point with { Y = 0 }).Length() <= PitRadius + 0.08f)
				return hole;
		}
		return -1;
	}

	private static ArrayMesh TopMesh()
	{
		int columns = Mathf.RoundToInt(HalfWidth * 2 / MeshCell);
		int rows = Mathf.RoundToInt(HalfLength * 2 / MeshCell);
		var tool = new SurfaceTool();
		tool.Begin(Mesh.PrimitiveType.Triangles);
		for (int r = 0; r <= rows; r++)
		{
			for (int c = 0; c <= columns; c++)
			{
				float x = -HalfWidth + c * MeshCell;
				float z = -HalfLength + r * MeshCell;
				tool.SetUV(new Vector2((float)c / columns, (float)r / rows));
				tool.AddVertex(new Vector3(x, SurfaceHeight(x, z), z));
			}
		}
		for (int r = 0; r < rows; r++)
		{
			for (int c = 0; c < columns; c++)
			{
				int i = r * (columns + 1) + c;
				int below = i + columns + 1;
				tool.AddIndex(i);
				tool.AddIndex(i + 1);
				tool.AddIndex(below);
				tool.AddIndex(i + 1);
				tool.AddIndex(below + 1);
				tool.AddIndex(below);
			}
		}
		tool.GenerateNormals();
		return tool.Commit();
	}

	/// <summary>A height map of the same surface. Heights are divided by the cell size because the shape is scaled up by it.</summary>
	private static HeightMapShape3D TopCollision()
	{
		int width = Mathf.RoundToInt(HalfWidth * 2 / CollisionCell) + 1;
		int depth = Mathf.RoundToInt(HalfLength * 2 / CollisionCell) + 1;
		var data = new float[width * depth];
		for (int r = 0; r < depth; r++)
		{
			for (int c = 0; c < width; c++)
			{
				float x = -HalfWidth + c * CollisionCell;
				float z = -HalfLength + r * CollisionCell;
				data[r * width + c] = SurfaceHeight(x, z) / CollisionCell;
			}
		}
		return new HeightMapShape3D { MapWidth = width, MapDepth = depth, MapData = data };
	}
}
