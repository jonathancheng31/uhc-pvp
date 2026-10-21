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

    public Box Grow(double d) => new(MinX - d, MinY - d, MinZ - d, MaxX + d, MaxY + d, MaxZ + d);

    /// <summary>Distance from a point to the nearest point of this box (0 if inside).</summary>
    public double DistanceTo(Vec3d p)
    {
        double dx = Math.Max(Math.Max(MinX - p.X, 0), p.X - MaxX);
        double dy = Math.Max(Math.Max(MinY - p.Y, 0), p.Y - MaxY);
        double dz = Math.Max(Math.Max(MinZ - p.Z, 0), p.Z - MaxZ);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public bool Contains(Vec3d p) =>
        p.X > MinX && p.X < MaxX && p.Y > MinY && p.Y < MaxY && p.Z > MinZ && p.Z < MaxZ;

    /// <summary>
    /// Distance along a ray (unit <paramref name="direction"/>) to where it enters this box, or
    /// null if it misses or the entry is further than <paramref name="maxDistance"/>.
    /// </summary>
    public double? RayEntry(Vec3d origin, Vec3d direction, double maxDistance)
    {
        double enter = 0;
        double exit = maxDistance;
        if (!Slab(origin.X, direction.X, MinX, MaxX, ref enter, ref exit)
            || !Slab(origin.Y, direction.Y, MinY, MaxY, ref enter, ref exit)
            || !Slab(origin.Z, direction.Z, MinZ, MaxZ, ref enter, ref exit))
        {
            return null;
        }
        return enter;
    }

    private static bool Slab(double origin, double direction, double min, double max, ref double enter, ref double exit)
    {
        if (Math.Abs(direction) < 1e-12)
        {
            return origin >= min && origin <= max;
        }
        double t1 = (min - origin) / direction;
        double t2 = (max - origin) / direction;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }
        enter = Math.Max(enter, t1);
        exit = Math.Min(exit, t2);
        return enter <= exit;
    }

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

public static class CollisionWorldExtensions
{
    /// <summary>
    /// How far a box can move: slides it along Y, then X, then Z, stopping at every solid box
    /// (Minecraft's Entity.moveEntity). Doesn't move anything. <paramref name="scratch"/> is a
    /// reusable list, so moving things every tick doesn't allocate.
    /// </summary>
    public static (double X, double Y, double Z) Clip(
        this ICollisionWorld world, Box box, double dx, double dy, double dz, List<Box> scratch)
    {
        scratch.Clear();
        world.CollectBoxes(box.Stretch(dx, dy, dz), scratch);
        foreach (Box solid in scratch)
        {
            dy = solid.ClipY(box, dy);
        }
        box = box.Offset(0, dy, 0);
        foreach (Box solid in scratch)
        {
            dx = solid.ClipX(box, dx);
        }
        box = box.Offset(dx, 0, 0);
        foreach (Box solid in scratch)
        {
            dz = solid.ClipZ(box, dz);
        }
        return (dx, dy, dz);
    }
}
