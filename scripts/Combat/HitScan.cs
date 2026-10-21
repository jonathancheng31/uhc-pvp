using System;
using System.Collections.Generic;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Combat;

public enum AimKind
{
    /// <summary>Nothing in reach: clicking here costs Minecraft 1.8's miss delay.</summary>
    Miss,
    /// <summary>Pointing at a block within block reach.</summary>
    Block,
    /// <summary>Pointing at a player within attack reach.</summary>
    Player,
}

public readonly record struct AimTarget(int Id, Vec3d Feet);

public readonly record struct AimResult(AimKind Kind, int TargetId, double Distance);

/// <summary>
/// What the crosshair points at, following Minecraft 1.8.9's EntityRenderer.getMouseOver:
/// blocks are traced up to 4.5 blocks, players' hitboxes are enlarged by 0.1 on every side, a
/// player only counts if closer than the first block, and only within 3 blocks of the eyes.
/// </summary>
public static class HitScan
{
    public static AimResult Aim(Vec3d eye, Vec3d look, IReadOnlyList<AimTarget> targets, VoxelWorld world)
    {
        double blockDistance = Raycast(world, eye, look, Tuning.BlockReach);
        double limit = Math.Min(blockDistance, Tuning.BlockReach);
        int bestId = 0;
        double best = limit;
        bool found = false;
        foreach (AimTarget target in targets)
        {
            Box box = Hitbox(target.Feet);
            if (box.Contains(eye))
            {
                if (best >= 0)
                {
                    bestId = target.Id;
                    best = 0;
                    found = true;
                }
                continue;
            }
            double? hit = box.RayEntry(eye, look, Tuning.BlockReach);
            if (hit is double d && (d < best || best == 0))
            {
                bestId = target.Id;
                best = d;
                found = true;
            }
        }
        if (found)
        {
            // Pointing at a player out of reach counts as pointing at nothing, even with a block behind.
            return best > Tuning.Reach
                ? new AimResult(AimKind.Miss, 0, best)
                : new AimResult(AimKind.Player, bestId, best);
        }
        return blockDistance <= Tuning.BlockReach
            ? new AimResult(AimKind.Block, 0, blockDistance)
            : new AimResult(AimKind.Miss, 0, double.PositiveInfinity);
    }

    /// <summary>A player's hitbox for aiming: their collision box plus the 0.1 border.</summary>
    public static Box Hitbox(Vec3d feet) =>
        Box.ForPlayer(feet, Tuning.PlayerWidth, Tuning.PlayerHeight).Grow(Tuning.HitboxBorder);

    /// <summary>The unit vector a player looks along. Yaw 0 faces -Z, positive pitch looks up.</summary>
    public static Vec3d LookVector(double yawDegrees, double pitchDegrees)
    {
        double pitch = pitchDegrees * Math.PI / 180.0;
        Vec3d flat = Vec3d.Forward(yawDegrees);
        return new Vec3d(flat.X * Math.Cos(pitch), Math.Sin(pitch), flat.Z * Math.Cos(pitch));
    }

    /// <summary>
    /// Distance along the ray to the first solid block, or infinity if none within
    /// <paramref name="maxDistance"/>. Steps through the grid one block boundary at a time.
    /// </summary>
    public static double Raycast(VoxelWorld world, Vec3d origin, Vec3d direction, double maxDistance)
    {
        int x = (int)Math.Floor(origin.X);
        int y = (int)Math.Floor(origin.Y);
        int z = (int)Math.Floor(origin.Z);
        if (world.IsSolid(x, y, z))
        {
            return 0;
        }
        int stepX = Math.Sign(direction.X);
        int stepY = Math.Sign(direction.Y);
        int stepZ = Math.Sign(direction.Z);
        double tMaxX = Boundary(origin.X, direction.X, x);
        double tMaxY = Boundary(origin.Y, direction.Y, y);
        double tMaxZ = Boundary(origin.Z, direction.Z, z);
        double tDeltaX = direction.X != 0 ? Math.Abs(1 / direction.X) : double.PositiveInfinity;
        double tDeltaY = direction.Y != 0 ? Math.Abs(1 / direction.Y) : double.PositiveInfinity;
        double tDeltaZ = direction.Z != 0 ? Math.Abs(1 / direction.Z) : double.PositiveInfinity;
        while (true)
        {
            double t;
            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                t = tMaxX;
                x += stepX;
                tMaxX += tDeltaX;
            }
            else if (tMaxY <= tMaxZ)
            {
                t = tMaxY;
                y += stepY;
                tMaxY += tDeltaY;
            }
            else
            {
                t = tMaxZ;
                z += stepZ;
                tMaxZ += tDeltaZ;
            }
            if (t > maxDistance)
            {
                return double.PositiveInfinity;
            }
            if (world.IsSolid(x, y, z))
            {
                return t;
            }
        }
    }

    private static double Boundary(double origin, double direction, int cell)
    {
        if (direction > 0)
        {
            return (cell + 1 - origin) / direction;
        }
        if (direction < 0)
        {
            return (cell - origin) / direction;
        }
        return double.PositiveInfinity;
    }
}
