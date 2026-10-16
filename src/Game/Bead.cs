using System;
using Godot;
using Renegade.Looks;
using static Mancala.Game.BoardLayout;

namespace Mancala.Game;

/// <summary>The marbles' shape, chosen in Settings. Each rolls (or tumbles) according to its shape.</summary>
public enum BeadShape
{
	Round,
	Pebble,
	Gem,
	Cube,
	Bean,
}

/// <summary>
/// One marble: a real physics body that rolls into its pit and settles in a little pile. While
/// it's carried (sown or captured) it's frozen and moved by tweens; dropping it hands it back to
/// physics, and its first landing is reported so it can be heard. Once it has come to rest it
/// freezes in place, so the board can be spun with every marble riding along.
/// </summary>
public partial class Bead : RigidBody3D
{
	/// <summary>How long a marble must stay still before it's frozen in place.</summary>
	private const float RestTime = 0.4f;

	public static readonly string[] ShapeNames = { "Round", "Pebble", "Gem", "Cube", "Bean" };

	private static readonly Mesh?[] Meshes = new Mesh?[ShapeNames.Length];
	private static readonly Shape3D?[] Shapes = new Shape3D?[ShapeNames.Length];

	/// <summary>Raised once per drop with the landing speed and what it hit.</summary>
	public event Action<Bead, float, Node>? Landed;

	private int _hole;

	/// <summary>The hole the rules say this marble is in.</summary>
	public int Hole
	{
		get => _hole;
		set
		{
			_hole = value;
			Returns = 0;
		}
	}

	/// <summary>How many times this marble has been put back after leaving its hole (it gives up after a few).</summary>
	public int Returns { get; set; }

	/// <summary>True from a drop until the marble has come to rest.</summary>
	public bool Moving => !Freeze;

	public MeshInstance3D Mesh { get; } = new();

	private readonly CollisionShape3D _collision = new();
	/// <summary>Where a resting marble sits on the board (board space), re-applied every physics tick.</summary>
	private Transform3D _rest;
	private bool _resting;
	private bool _falling;
	private float _fallSpeed;
	private float _still;

	public void Setup(Style style, BeadShape shape)
	{
		AddChild(Mesh);
		AddChild(_collision);
		Mass = 0.02f;
		GravityScale = 3f;
		LinearDamp = 0.6f;
		// Real marbles roll for ages; these settle quickly so a turn doesn't wait on them.
		AngularDamp = 4f;
		ContinuousCd = true;
		ContactMonitor = true;
		MaxContactsReported = 2;
		FreezeMode = FreezeModeEnum.Kinematic;
		BodyEntered += OnBodyEntered;
		ApplyStyle(style);
		ApplyShape(shape);
	}

	public void ApplyStyle(Style style)
	{
		Mesh.MaterialOverride = Materials.For(Styles.Solo(style).Look);
		PhysicsMaterialOverride = new PhysicsMaterial { Bounce = Styles.Bounce(style) * 0.6f, Friction = 0.8f, Rough = true };
	}

	public void ApplyShape(BeadShape shape)
	{
		Mesh.Mesh = MeshFor(shape);
		_collision.Shape = ShapeFor(shape);
		// Odd shapes land any way up.
		if (shape != BeadShape.Round)
			Rotation = new Vector3((float)GD.RandRange(0, Mathf.Tau), (float)GD.RandRange(0, Mathf.Tau), 0);
	}

	private static Mesh MeshFor(BeadShape shape) => Meshes[(int)shape] ??= shape switch
	{
		// A smooth stone: a flattened ball.
		BeadShape.Pebble => new SphereMesh { Radius = BeadRadius * 1.15f, Height = BeadRadius * 1.3f, RadialSegments = 24, Rings = 12 },
		// A cut gem: few facets, each lit flat so they sparkle.
		BeadShape.Gem => Faceted(new SphereMesh { Radius = BeadRadius * 1.05f, Height = BeadRadius * 2f, RadialSegments = 8, Rings = 3 }),
		BeadShape.Cube => Faceted(new BoxMesh { Size = Vector3.One * BeadRadius * 1.55f }),
		BeadShape.Bean => new CapsuleMesh { Radius = BeadRadius * 0.72f, Height = BeadRadius * 2.6f, RadialSegments = 16, Rings = 6 },
		_ => new SphereMesh { Radius = BeadRadius, Height = BeadRadius * 2, RadialSegments = 24, Rings = 12 },
	};

	private static Shape3D ShapeFor(BeadShape shape) => Shapes[(int)shape] ??= shape switch
	{
		BeadShape.Pebble or BeadShape.Gem => MeshFor(shape).CreateConvexShape(clean: true, simplify: true),
		BeadShape.Cube => new BoxShape3D { Size = Vector3.One * BeadRadius * 1.55f },
		BeadShape.Bean => new CapsuleShape3D { Radius = BeadRadius * 0.72f, Height = BeadRadius * 2.6f },
		_ => new SphereShape3D { Radius = BeadRadius },
	};

	/// <summary>Splits every corner so each face gets its own flat normal.</summary>
	private static Mesh Faceted(PrimitiveMesh mesh)
	{
		var tool = new SurfaceTool();
		tool.CreateFrom(mesh, 0);
		tool.Deindex();
		tool.GenerateNormals();
		return tool.Commit();
	}

	/// <summary>Takes the marble out of physics so a tween can carry it.</summary>
	public void Hold()
	{
		FreezeMode = FreezeModeEnum.Kinematic;
		Freeze = true;
		_resting = false;
		_falling = false;
	}

	/// <summary>Lets go: physics takes over from where the tween left it.</summary>
	public void Drop(Vector3 velocity)
	{
		Freeze = false;
		_resting = false;
		Sleeping = false;
		LinearVelocity = velocity;
		AngularVelocity = Vector3.Zero;
		_falling = true;
		_still = 0f;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Freeze)
		{
			// A frozen body follows its parent on desktop, but on the phone (Jolt on Android) the
			// physics server kept each resting marble where it was in the room while the board
			// turned under it. Pinning it back to its spot on the board every tick makes it ride along.
			if (_resting)
				Transform = _rest;
			return;
		}
		if (_falling)
			_fallSpeed = LinearVelocity.Length();
		_still = LinearVelocity.Length() < 0.08f && AngularVelocity.Length() < 0.6f ? _still + (float)delta : 0f;
		if (_still >= RestTime && !_falling)
		{
			// Static, not kinematic: a kinematic body is moved by the physics step and reported back a
			// tick late, so while the board spun, marbles trailed behind their bowls (on the phone).
			// A static one is placed directly, so it turns exactly with the board.
			FreezeMode = FreezeModeEnum.Static;
			Freeze = true;
			_rest = Transform;
			_resting = true;
		}
	}

	private void OnBodyEntered(Node body)
	{
		if (!_falling)
			return;
		_falling = false;
		Landed?.Invoke(this, _fallSpeed, body);
	}
}
