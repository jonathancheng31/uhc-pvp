using System;
using System.Collections.Generic;

namespace UhcPvp.Movement;

/// <summary>
/// An axis-aligned box in double precision. The offset methods follow Minecraft 1.8.9's
/// AxisAlignedBB exactly, so collisions behave the same.
/// </summary>
public readonly record struct Box(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    /// <summary>A player-sized box standing with its feet centered on <paramref name="feet"/>.</summary>
    public static Box ForPlayer(Vec3d feet, double width, double height)
    {
        double half = width / 2.0;
        return new Box(feet.X - half, feet.Y, feet.Z - half, feet.X + half, feet.Y + height, feet.Z + half);
    }

    public Box Offset(double x, double y, double z) =>
        new(MinX + x, MinY + y, MinZ + z, MaxX + x, MaxY + y, MaxZ + z);

    /// <summary>Grows the box in the direction of a movement, covering everything it sweeps through.</summary>
    public Box Stretch(double x, double y, double z) => new(
        x < 0 ? MinX + x : MinX, y < 0 ? MinY + y : MinY, z < 0 ? MinZ + z : MinZ,
        x > 0 ? MaxX + x : MaxX, y > 0 ? MaxY + y : MaxY, z > 0 ? MaxZ + z : MaxZ);

    public bool Intersects(Box o) =>
        o.MaxX > MinX && o.MinX < MaxX && o.MaxY > MinY && o.MinY < MaxY && o.MaxZ > MinZ && o.MinZ < MaxZ;

    /// <summary>
    /// How far <paramref name="mover"/> can travel along X (up to <paramref name="offset"/>)
    /// before hitting this box.
    /// </summary>
    public double ClipX(Box mover, double offset)
    {
        if (mover.MaxY <= MinY || mover.MinY >= MaxY || mover.MaxZ <= MinZ || mover.MinZ >= MaxZ)
        {
            return offset;
        }
        if (offset > 0 && mover.MaxX <= MinX)
        {
            return Math.Min(offset, MinX - mover.MaxX);
        }
        if (offset < 0 && mover.MinX >= MaxX)
        {
            return Math.Max(offset, MaxX - mover.MinX);
        }
        return offset;
    }

    public double ClipY(Box mover, double offset)
    {
        if (mover.MaxX <= MinX || mover.MinX >= MaxX || mover.MaxZ <= MinZ || mover.MinZ >= MaxZ)
        {
            return offset;
        }
        if (offset > 0 && mover.MaxY <= MinY)
        {
            return Math.Min(offset, MinY - mover.MaxY);
        }
        if (offset < 0 && mover.MinY >= MaxY)
        {
            return Math.Max(offset, MaxY - mover.MinY);
        }
        return offset;
    }

    public double ClipZ(Box mover, double offset)
    {
        if (mover.MaxX <= MinX || mover.MinX >= MaxX || mover.MaxY <= MinY || mover.MinY >= MaxY)
        {
            return offset;
        }
        if (offset > 0 && mover.MaxZ <= MinZ)
        {
            return Math.Min(offset, MinZ - mover.MaxZ);
        }
        if (offset < 0 && mover.MinZ >= MaxZ)
        {
            return Math.Max(offset, MaxZ - mover.MinZ);
        }
        return offset;
    }
}

/// <summary>Anything players can collide with.</summary>
public interface ICollisionWorld
{
    /// <summary>Adds the boxes of every solid thing overlapping <paramref name="region"/> to <paramref name="output"/>.</summary>
    void CollectBoxes(Box region, List<Box> output);
}
