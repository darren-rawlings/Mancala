using System;
using Godot;
using static Mancala.Game.BoardLayout;

namespace Mancala.Game;

/// <summary>
/// One glass gem: a real physics body that tumbles into its pit and settles in a little pile.
/// While it's being carried (sown or captured) it's frozen and moved by tweens; dropping it hands
/// it back to physics, and its first landing is reported so it can be heard.
/// </summary>
public partial class Stone : RigidBody3D
{
	/// <summary>Raised once per drop with the landing speed and what it hit.</summary>
	public event Action<Stone, float, Node>? Landed;

	/// <summary>The hole the rules say this stone is in.</summary>
	public int Hole { get; set; }

	public MeshInstance3D Mesh { get; } = new();

	private bool _falling;
	private float _fallSpeed;

	public void Setup(int colorIndex)
	{
		// A gem is a squashed sphere: domed on top, flat enough to stack.
		Mesh.Mesh = new SphereMesh { Radius = StoneRadius, Height = StoneHeight, RadialSegments = 20, Rings = 10 };
		Mesh.MaterialOverride = Looks.Gem(colorIndex);
		Mesh.RotationDegrees = new Vector3(0, GD.Randf() * 360f, 0);
		AddChild(Mesh);
		AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = StoneRadius * 0.92f, Height = StoneHeight * 0.85f } });

		Mass = 0.02f;
		GravityScale = 3f;
		LinearDamp = 0.8f;
		AngularDamp = 3f;
		ContinuousCd = true;
		ContactMonitor = true;
		MaxContactsReported = 2;
		FreezeMode = FreezeModeEnum.Kinematic;
		PhysicsMaterialOverride = new PhysicsMaterial { Bounce = 0.12f, Friction = 0.7f };
		BodyEntered += OnBodyEntered;
	}

	/// <summary>Takes the stone out of physics so a tween can carry it.</summary>
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
		AngularVelocity = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * 4f;
		_falling = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_falling)
			_fallSpeed = LinearVelocity.Length();
	}

	private void OnBodyEntered(Node body)
	{
		if (!_falling)
			return;
		_falling = false;
		Landed?.Invoke(this, _fallSpeed, body);
	}
}
