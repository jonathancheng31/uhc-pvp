using System;
using Godot;

namespace UhcPvp.Movement;

/// <summary>A double-precision vector for physics, like Minecraft's own math.</summary>
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static readonly Vec3d Zero = new(0, 0, 0);

    public double HorizontalLength => Math.Sqrt(X * X + Z * Z);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3d operator *(double s, Vec3d a) => a * s;

    public Vec3d WithY(double y) => this with { Y = y };

    public Vector3 ToGodot() => new((float)X, (float)Y, (float)Z);

    public static Vec3d From(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Horizontal unit vector a player faces at this yaw (degrees; 0 faces -Z, positive turns left).</summary>
    public static Vec3d Forward(double yawDegrees)
    {
        double yaw = yawDegrees * Math.PI / 180.0;
        return new Vec3d(-Math.Sin(yaw), 0, -Math.Cos(yaw));
    }

    /// <summary>Horizontal unit vector to the player's left at this yaw.</summary>
    public static Vec3d Left(double yawDegrees)
    {
        double yaw = yawDegrees * Math.PI / 180.0;
        return new Vec3d(-Math.Cos(yaw), 0, Math.Sin(yaw));
    }
}
