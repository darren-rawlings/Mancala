using System;
using Godot;
using static Mancala.Game.BoardLayout;

namespace Mancala.Game;

/// <summary>
/// One marble: a real physics sphere that rolls into its pit and settles in a little pile. While
/// it's carried (sown or captured) it's frozen and moved by tweens; dropping it hands it back to
/// physics, and its first landing is reported so it can be heard. Once it has come to rest it
/// freezes in place, so the board can be spun with every marble riding along.
/// </summary>
public partial class Bead : RigidBody3D
{
	/// <summary>How long a marble must stay still before it's frozen in place.</summary>
	private const float RestTime = 0.4f;

	private static readonly SphereMesh Sphere = new() { Radius = BeadRadius, Height = BeadRadius * 2, RadialSegments = 24, Rings = 12 };

	/// <summary>Raised once per drop with the landing speed and what it hit.</summary>
	public event Action<Bead, float, Node>? Landed;

	/// <summary>The hole the rules say this marble is in.</summary>
	public int Hole { get; set; }

	/// <summary>True from a drop until the marble has come to rest.</summary>
	public bool Moving => !Freeze;

	public MeshInstance3D Mesh { get; } = new() { Mesh = Sphere };

	private bool _falling;
	private float _fallSpeed;
	private float _still;

	public void Setup(Style style)
	{
		AddChild(Mesh);
		AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = BeadRadius } });
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
	}

	public void ApplyStyle(Style style)
	{
		Mesh.MaterialOverride = Looks.For(Styles.BeadLook(style));
		PhysicsMaterialOverride = new PhysicsMaterial { Bounce = Styles.Bounce(style) * 0.6f, Friction = 0.8f, Rough = true };
	}

	/// <summary>Takes the marble out of physics so a tween can carry it.</summary>
	public void Hold()
	{
		Freeze = true;
		_falling = false;
	}

	/// <summary>Lets go: physics takes over from where the tween left it.</summary>
	public void Drop(Vector3 velocity)
	{
		Freeze = false;
		Sleeping = false;
		LinearVelocity = velocity;
		AngularVelocity = Vector3.Zero;
		_falling = true;
		_still = 0f;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Freeze)
			return;
		if (_falling)
			_fallSpeed = LinearVelocity.Length();
		_still = LinearVelocity.Length() < 0.08f && AngularVelocity.Length() < 0.6f ? _still + (float)delta : 0f;
		if (_still >= RestTime && !_falling)
			Freeze = true;
	}

	private void OnBodyEntered(Node body)
	{
		if (!_falling)
			return;
		_falling = false;
		Landed?.Invoke(this, _fallSpeed, body);
	}
}
