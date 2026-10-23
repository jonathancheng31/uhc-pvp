using System;
using Godot;

namespace UhcPvp.Core;

[Flags]
public enum PlayerFlags : byte
{
    None = 0,
    OnGround = 1,
    Sprinting = 2,
    Sneaking = 4,
    Dead = 8,
    /// <summary>Flying from a knockback, until landing: pushing leaves them alone (see PlayerMotor.KnockbackFlight).</summary>
    KnockbackFlight = 16,
}

/// <summary>
/// The compact state each player sends many times per second.
/// Yaw and pitch are in degrees. Yaw 0 faces -Z; positive yaw turns left. Positive pitch looks up.
/// Health is in half-hearts (0 to 20).
/// </summary>
public readonly record struct PlayerState(Vector3 Position, float Yaw, float Pitch, PlayerFlags Flags, float Health)
{
    public bool IsValid =>
        float.IsFinite(Position.X) && float.IsFinite(Position.Y) && float.IsFinite(Position.Z)
        && float.IsFinite(Yaw) && float.IsFinite(Pitch) && float.IsFinite(Health);

    public bool Has(PlayerFlags flag) => (Flags & flag) != 0;

    /// <summary>Blends two states; yaw takes the short way round.</summary>
    public static PlayerState Blend(PlayerState a, PlayerState b, float t)
    {
        float yaw = a.Yaw + Mathf.Wrap(b.Yaw - a.Yaw, -180f, 180f) * t;
        return new PlayerState(
            a.Position.Lerp(b.Position, t),
            yaw,
            Mathf.Lerp(a.Pitch, b.Pitch, t),
            t < 0.5f ? a.Flags : b.Flags,
            t < 0.5f ? a.Health : b.Health);
    }
}
