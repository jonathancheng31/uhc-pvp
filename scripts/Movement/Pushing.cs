using System;
using UhcPvp.Core;

namespace UhcPvp.Movement;

/// <summary>
/// Players pushing each other apart, the "Player pushing" twist. 1.8.9 has none (players walk
/// through each other); Minecraft 1.9 added a gentle push. Ours pushes harder the deeper two
/// players overlap, so at full strength you can't stand inside someone. Every game pushes only
/// its own player (and its bots), away from where it sees the others, so it needs nothing
/// extra on the network.
/// </summary>
public static class Pushing
{
    /// <summary>
    /// The push on a player whose feet are at <paramref name="self"/> from another player at
    /// <paramref name="other"/>, in Minecraft units (blocks per tick, added once per Minecraft
    /// tick). Zero unless their boxes overlap. <paramref name="strength"/> is the match rule: 0 is
    /// off (1.8.9), 1 is <see cref="Tuning.PushImpulse"/> at full overlap.
    /// </summary>
    public static Vec3d Away(Vec3d self, Vec3d other, double strength, bool onGround)
    {
        if (strength <= 0)
        {
            return Vec3d.Zero;
        }
        double dx = self.X - other.X;
        double dz = self.Z - other.Z;
        // The boxes overlap when they're closer than a body width on both axes (and in height).
        double depth = Tuning.PlayerWidth - Math.Max(Math.Abs(dx), Math.Abs(dz));
        if (depth <= 0 || Math.Abs(self.Y - other.Y) >= Tuning.PlayerHeight)
        {
            return Vec3d.Zero;
        }
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance < Tuning.PushMinDistance)
        {
            return Vec3d.Zero;
        }
        double impulse = strength * Tuning.PushImpulse * depth / Tuning.PlayerWidth
            * (onGround ? 1 : Tuning.PushAirFactor);
        return new Vec3d(dx / distance * impulse, 0, dz / distance * impulse);
    }
}
