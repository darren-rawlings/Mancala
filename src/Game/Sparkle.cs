using Godot;

namespace Mancala.Game;

/// <summary>
/// The "pit stop" glow: a warm halo on a captured gem plus a trail of sparks left in the air
/// behind it as it flies to the store. The sparks live in world space, so they hang where the gem
/// was and fade, drawing the arc.
/// </summary>
public partial class Sparkle : Node3D
{
	private static StandardMaterial3D? _halo;
	private static StandardMaterial3D? _spark;

	public static StandardMaterial3D Halo => _halo ??= new StandardMaterial3D
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		BlendMode = BaseMaterial3D.BlendModeEnum.Add,
		AlbedoColor = new Color(0.85f, 0.5f, 0.12f),
		CullMode = BaseMaterial3D.CullModeEnum.Back,
	};

	private static StandardMaterial3D Spark => _spark ??= new StandardMaterial3D
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		BlendMode = BaseMaterial3D.BlendModeEnum.Add,
		BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
		VertexColorUseAsAlbedo = true,
		AlbedoTexture = SoftDot(),
	};

	private readonly CpuParticles3D _trail = new()
	{
		Amount = 40,
		Lifetime = 0.75f,
		LocalCoords = false,
		Mesh = new QuadMesh { Size = new Vector2(0.2f, 0.2f), Material = Spark },
		EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
		EmissionSphereRadius = 0.05f,
		Spread = 180f,
		InitialVelocityMin = 0.1f,
		InitialVelocityMax = 0.4f,
		Gravity = new Vector3(0, 0.6f, 0),
		CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		ColorRamp = Ramp(),
	};

	public override void _Ready() => AddChild(_trail);

	/// <summary>Stops making sparks; the last ones fade out, then the node removes itself.</summary>
	public void Finish()
	{
		_trail.Emitting = false;
		GetTree().CreateTimer(_trail.Lifetime + 0.1).Timeout += QueueFree;
	}

	/// <summary>A one-off spray of sparks, for the moment a capture happens.</summary>
	public static void Burst(Node parent, Vector3 position)
	{
		var burst = new CpuParticles3D
		{
			Position = position,
			Amount = 36,
			Lifetime = 0.8f,
			OneShot = true,
			Explosiveness = 0.9f,
			LocalCoords = false,
			Mesh = new QuadMesh { Size = new Vector2(0.18f, 0.18f), Material = Spark },
			Direction = Vector3.Up,
			Spread = 60f,
			InitialVelocityMin = 1.5f,
			InitialVelocityMax = 3.2f,
			Gravity = new Vector3(0, -4f, 0),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			ColorRamp = Ramp(),
		};
		parent.AddChild(burst);
		burst.Emitting = true;
		parent.GetTree().CreateTimer(1.2).Timeout += burst.QueueFree;
	}

	private static Gradient Ramp()
	{
		var gradient = new Gradient();
		gradient.Offsets = new[] { 0f, 0.3f, 1f };
		gradient.Colors = new[] { new Color(1f, 0.95f, 0.7f, 1f), new Color(1f, 0.65f, 0.2f, 0.8f), new Color(0.9f, 0.3f, 0.05f, 0f) };
		return gradient;
	}

	private static GradientTexture2D SoftDot()
	{
		var gradient = new Gradient();
		gradient.SetColor(0, Colors.White);
		gradient.SetColor(1, new Color(1, 1, 1, 0));
		return new GradientTexture2D
		{
			Gradient = gradient,
			Fill = GradientTexture2D.FillEnum.Radial,
			FillFrom = new Vector2(0.5f, 0.5f),
			FillTo = new Vector2(1f, 0.5f),
			Width = 32,
			Height = 32,
		};
	}
}
