using Godot;

namespace UhcPvp.Gameplay;

/// <summary>One-off particles. Sounds are played by <see cref="Sounds"/>.</summary>
public static class Effects
{
    /// <summary>A hurt sound, for the volume slider's preview in the settings.</summary>
    public const string HurtSoundPath = "res://assets/sounds/hurt_1.wav";

    /// <summary>A burst of small sparks around a critical hit.</summary>
    public static void CritBurst(Node parent, Vector3 position)
    {
        var particles = new CpuParticles3D
        {
            Position = position,
            OneShot = true,
            Emitting = false,
            Amount = 18,
            Lifetime = 0.5,
            Explosiveness = 0.9f,
            Direction = Vector3.Up,
            Spread = 180,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 3.5f,
            Gravity = new Vector3(0, -6, 0),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.0f,
            Color = new Color(1f, 0.95f, 0.75f),
            Mesh = new QuadMesh
            {
                Size = new Vector2(0.07f, 0.07f),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                    VertexColorUseAsAlbedo = true,
                },
            },
        };
        parent.AddChild(particles);
        particles.Emitting = true;
        particles.GetTree().CreateTimer(1.0).Timeout += particles.QueueFree;
    }
}
