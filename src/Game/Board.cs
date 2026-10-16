using System.Collections.Generic;
using Godot;
using Mancala.Core;
using Renegade.Looks;
using static Mancala.Game.BoardLayout;

namespace Mancala.Game;

/// <summary>
/// The carved board: a smooth surface generated from <see cref="BoardLayout.SurfaceHeight"/> (the
/// same heights drive the collision, so marbles settle into exactly the bowls you see), a count
/// beside every hole, and rings that light up the pits you can play. It spins with the rig.
/// </summary>
public partial class Board : Node3D
{
	/// <summary>Visual mesh resolution. Fine enough that the bowls look round, not faceted.</summary>
	private const float MeshCell = 0.04f;

	/// <summary>Collision resolution. Coarser than the mesh: marbles only need the bowls' shape.</summary>
	private const float CollisionCell = 0.05f;

	private readonly Label3D[] _counts = new Label3D[Kalah.Holes];
	private readonly MeshInstance3D[] _rings = new MeshInstance3D[Kalah.Holes];
	private readonly List<MeshInstance3D> _surfaces = new();
	private readonly StandardMaterial3D _ringMaterial = new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		AlbedoColor = new Color(1f, 0.78f, 0.35f, 0.75f),
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
	};

	/// <summary>The stores' paint: each player's colour (gold near you, blue across the board).</summary>
	public static readonly Color StoreOneColour = new("b8862f");
	public static readonly Color StoreTwoColour = new("2f6aa8");

	public Style Style { get; private set; }

	/// <summary>The board's collision bodies, so impacts can tell "hit the board" from "hit another marble".</summary>
	public StaticBody3D Body { get; } = new();

	public void Build(Style style)
	{
		_surfaces.Add(new MeshInstance3D { Mesh = TopMesh() });
		// The body: a slab whose top sits below the deepest hole (right under the carved top it hid
		// every bowl), with thin walls round the edge stopping a hair under the top so they never
		// z-fight with it.
		float under = StoreDepth + 0.03f;
		_surfaces.Add(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(HalfX * 2, Thickness - under, HalfZ * 2) },
			Position = new Vector3(0, -under - (Thickness - under) / 2, 0),
		});
		const float wall = 0.02f;
		foreach (var (size, offset) in new[]
		{
			(new Vector3(HalfX * 2, under, wall), new Vector3(0, 0, HalfZ - wall / 2)),
			(new Vector3(HalfX * 2, under, wall), new Vector3(0, 0, -HalfZ + wall / 2)),
			(new Vector3(wall, under, HalfZ * 2), new Vector3(HalfX - wall / 2, 0, 0)),
			(new Vector3(wall, under, HalfZ * 2), new Vector3(-HalfX + wall / 2, 0, 0)),
		})
		{
			_surfaces.Add(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = offset + new Vector3(0, -under / 2 - 0.003f, 0) });
		}
		foreach (var surface in _surfaces)
			AddChild(surface);

		// Each store is painted in its owner's colour.
		foreach (var (store, colour) in new[] { (Kalah.StoreOne, StoreOneColour), (Kalah.StoreTwo, StoreTwoColour) })
		{
			AddChild(new MeshInstance3D
			{
				Mesh = StorePaint(store),
				MaterialOverride = Materials.For(new Look(Look.PlasticPattern, colour, colour.Darkened(0.35f), 0.6f)),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}

		Body.AddChild(new CollisionShape3D { Shape = TopCollision(), Scale = Vector3.One * CollisionCell });
		Body.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(HalfX * 2, Thickness - under, HalfZ * 2) },
			Position = new Vector3(0, -under - (Thickness - under) / 2, 0),
		});
		AddChild(Body);

		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			bool store = Kalah.IsStore(hole);
			var center = HoleCenter(hole);
			// Counts sit just outside each hole: in front of your row, behind the far row, and in
			// front of each store, so piles of marbles never hide them.
			var offset = store
				? new Vector3(0, 0.05f, StoreHalfZ + 0.2f)
				: new Vector3(0, 0.05f, Mathf.Sign(center.Z) * (PitRadius + 0.14f));
			_counts[hole] = new Label3D
			{
				Position = center + offset,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FontSize = store ? 110 : 84,
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
		ApplyStyle(style);
	}

	public void ApplyStyle(Style style)
	{
		Style = style;
		var material = Materials.For(Styles.BoardLook(style));
		foreach (var surface in _surfaces)
			surface.MaterialOverride = material;
		Body.PhysicsMaterialOverride = new PhysicsMaterial { Bounce = Styles.Bounce(style), Friction = 0.8f };
	}

	public void SetCount(int hole, int count) => _counts[hole].Text = count.ToString();

	/// <summary>Shows or hides the numbers by the pits and by the stores (Settings).</summary>
	public void ShowCounts(bool pits, bool stores)
	{
		for (int hole = 0; hole < Kalah.Holes; hole++)
			_counts[hole].Visible = Kalah.IsStore(hole) ? stores : pits;
	}

	/// <summary>Lights the rings round the pits that can be played now; pass nothing to turn them all off.</summary>
	public void HighlightPits(IEnumerable<int>? pits)
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

	/// <summary>The pit under a board-space point, if the point is inside one (with a little slack for fingers).</summary>
	public static int PitAt(Vector3 local)
	{
		for (int hole = 0; hole < Kalah.Holes; hole++)
		{
			if (!Kalah.IsStore(hole) && DistanceToRim(hole, local) <= 0.1f)
				return hole;
		}
		return -1;
	}

	/// <summary>
	/// A coat of paint lining a store: rings of the store's rounded-rectangle outline, each a little
	/// further in, following the carved surface just above it. Rings rather than a grid, so the
	/// paint's edge runs smoothly round the rim.
	/// </summary>
	private static ArrayMesh StorePaint(int store)
	{
		const int perCorner = 8;
		const float step = 0.02f;
		var center = HoleCenter(store);
		var rings = new List<Vector3[]>();
		for (float inset = 0f; ; inset += step)
		{
			inset = Mathf.Min(inset, StoreHalfX);
			float hx = StoreHalfX - inset, hz = StoreHalfZ - inset, corner = Mathf.Max(StoreCorner - inset, 0f);
			var ring = new Vector3[perCorner * 4];
			for (int c = 0; c < 4; c++)
			{
				float sx = c is 0 or 3 ? 1 : -1, sz = c < 2 ? 1 : -1;
				for (int k = 0; k < perCorner; k++)
				{
					float angle = (c + (float)k / (perCorner - 1)) * Mathf.Pi / 2;
					float x = center.X + sx * (hx - corner) + corner * Mathf.Cos(angle);
					float z = center.Z + sz * (hz - corner) + corner * Mathf.Sin(angle);
					ring[c * perCorner + k] = new Vector3(x, SurfaceHeight(x, z) + 0.005f, z);
				}
			}
			rings.Add(ring);
			if (inset >= StoreHalfX)
				break;
		}

		var tool = new SurfaceTool();
		tool.Begin(Mesh.PrimitiveType.Triangles);
		int count = perCorner * 4;
		foreach (var ring in rings)
		{
			foreach (var point in ring)
				tool.AddVertex(point);
		}
		for (int r = 0; r + 1 < rings.Count; r++)
		{
			for (int j = 0; j < count; j++)
			{
				int a = r * count + j, a2 = r * count + (j + 1) % count;
				int b = (r + 1) * count + j, b2 = (r + 1) * count + (j + 1) % count;
				tool.AddIndex(a);
				tool.AddIndex(a2);
				tool.AddIndex(b);
				tool.AddIndex(a2);
				tool.AddIndex(b2);
				tool.AddIndex(b);
			}
		}
		tool.GenerateNormals();
		return tool.Commit();
	}

	private static ArrayMesh TopMesh()
	{
		int columns = Mathf.RoundToInt(HalfX * 2 / MeshCell);
		int rows = Mathf.RoundToInt(HalfZ * 2 / MeshCell);
		var tool = new SurfaceTool();
		tool.Begin(Mesh.PrimitiveType.Triangles);
		for (int r = 0; r <= rows; r++)
		{
			for (int c = 0; c <= columns; c++)
			{
				float x = -HalfX + c * MeshCell;
				float z = -HalfZ + r * MeshCell;
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
		int width = Mathf.RoundToInt(HalfX * 2 / CollisionCell) + 1;
		int depth = Mathf.RoundToInt(HalfZ * 2 / CollisionCell) + 1;
		var data = new float[width * depth];
		for (int r = 0; r < depth; r++)
		{
			for (int c = 0; c < width; c++)
			{
				float x = -HalfX + c * CollisionCell;
				float z = -HalfZ + r * CollisionCell;
				data[r * width + c] = SurfaceHeight(x, z) / CollisionCell;
			}
		}
		return new HeightMapShape3D { MapWidth = width, MapDepth = depth, MapData = data };
	}
}
