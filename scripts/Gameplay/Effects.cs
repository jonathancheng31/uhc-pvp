using System;
using Godot;

namespace UhcPvp.Gameplay;

/// <summary>One-off sounds and particles. All sounds are our own (tools/gen_sounds.py).</summary>
public static class Effects
{
    public const string HurtSoundPath = "res://assets/sounds/hurt.wav";

    private static readonly Random Random = new();
    private static AudioStream? _hurt;

    /// <summary>The hurt sound at a spot, with Minecraft's small random pitch change.</summary>
    public static void PlayHurt(Node parent, Vector3 position, bool killed)
    {
        _hurt ??= GD.Load<AudioStream>(HurtSoundPath);
        float pitch = (float)((Random.NextDouble() - Random.NextDouble()) * 0.2 + 1.0);
        var player = new AudioStreamPlayer3D
        {
            Stream = _hurt,
            Position = position,
            PitchScale = killed ? pitch * 0.7f : pitch,
            UnitSize = 8,
            MaxDistance = 64,
        };
        parent.AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

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
