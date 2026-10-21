using System;
using System.Collections.Generic;
using UhcPvp.Core;
using UhcPvp.Movement;

namespace UhcPvp.Combat;

/// <summary>A player the bobber can catch: their id and where they're drawn on the thrower's screen.</summary>
public readonly record struct RodTarget(int Id, Vec3d Feet);

/// <summary>
/// The fishing rod's bobber. Our own rod, with flight numbers that start from 1.8.9's
/// EntityFishHook (see docs/rod.md).
///
/// The flight is worked out once per Minecraft tick (1/20 s) from the cast alone: every game
/// that hears about a cast computes the same path. Between those ticks it moves in a straight
/// line, which is also the path checked for players, the way Minecraft checks what a
/// projectile hits. Which player it catches is decided by the thrower's game (Game.Rod.cs);
/// a caught bobber hangs on that player until the thrower casts again.
/// </summary>
public sealed class Bobber
{
    private readonly ICollisionWorld _world;
    private readonly List<Box> _boxes = new();
    /// <summary>Host time the current flight started (the cast, or a release).</summary>
    private double _startTime;
    /// <summary>Which Minecraft tick of the flight <see cref="_from"/> and <see cref="_to"/> span.</summary>
    private long _tick;
    private Vec3d _from;
    private Vec3d _to;
    /// <summary>Motion for the tick after <see cref="_to"/>, in blocks per MC tick.</summary>
    private Vec3d _motion;

    public Bobber(ICollisionWorld world, int throwerId, double castTime, Vec3d origin, Vec3d velocity)
    {
        _world = world;
        ThrowerId = throwerId;
        CastTime = castTime;
        Fly(castTime, origin, velocity);
    }

    public int ThrowerId { get; }
    /// <summary>Host time of the cast.</summary>
    public double CastTime { get; }
    /// <summary>The player it's caught on, if any.</summary>
    public int? HookedId { get; private set; }
    /// <summary>Whether it's lying on the ground (or against a wall), as of the latest tick worked out.</summary>
    public bool Landed { get; private set; }

    /// <summary>The bobber's collision box, a small cube standing on <paramref name="position"/>.</summary>
    public static Box BoxAt(Vec3d position) => Box.ForPlayer(position, Tuning.BobberSize, Tuning.BobberSize);

    /// <summary>
    /// Where a cast starts: just below the eyes, at the left hand (our rod is in the left hand;
    /// Minecraft's starts at the right hand, 0.16 to the side).
    /// </summary>
    public static Vec3d CastOrigin(Vec3d eye, double yaw) =>
        eye + new Vec3d(0, -Tuning.RodCastDrop, 0) + Vec3d.Left(yaw) * Tuning.RodCastSideOffset;

    /// <summary>
    /// The launch motion: along the view, with a tiny random spread, at the cast speed
    /// (EntityFishHook's constructor and handleHookCasting).
    /// </summary>
    public static Vec3d CastVelocity(double yaw, double pitch, Random random) =>
        CastVelocity(yaw, pitch, new Vec3d(Gaussian(random), Gaussian(random), Gaussian(random)));

    /// <summary>The launch motion for given random spread (three standard normal numbers).</summary>
    public static Vec3d CastVelocity(double yaw, double pitch, Vec3d noise) =>
        (HitScan.LookVector(yaw, pitch) + noise * Tuning.RodCastSpread) * Tuning.RodCastSpeed;

    /// <summary>
    /// Where the flying bobber is at host time <paramref name="time"/>. Times before the flight
    /// started give its start; works out new ticks as needed (times should mostly go forward).
    /// </summary>
    public Vec3d PositionAt(double time)
    {
        double ticks = (time - _startTime) * Tuning.McTickRate;
        if (!(ticks > 0))
        {
            return _from;
        }
        long tick = (long)Math.Floor(ticks);
        while (_tick < tick)
        {
            _from = _to;
            _to = Step(_to);
            _tick++;
        }
        if (tick < _tick)
        {
            return _from;
        }
        double t = ticks - tick;
        return _from + (_to - _from) * t;
    }

    /// <summary>Caught on a player: it hangs on them from now on.</summary>
    public void Hook(int playerId) => HookedId = playerId;

    /// <summary>
    /// The caught player is gone (died or left): the bobber drops from where it hung, starting
    /// at host time <paramref name="time"/>.
    /// </summary>
    public void Release(double time, Vec3d position)
    {
        HookedId = null;
        Fly(time, position, Vec3d.Zero);
    }

    /// <summary>Where a caught bobber hangs on a player standing at <paramref name="feet"/>.</summary>
    public static Vec3d HookPoint(Vec3d feet) => feet + new Vec3d(0, Tuning.PlayerHeight * Tuning.BobberHookHeight, 0);

    /// <summary>
    /// The first target a bobber moving in a straight line from <paramref name="from"/> to
    /// <paramref name="to"/> touches, and how far along the move (0 to 1). Players count
    /// <see cref="Tuning.BobberHitboxGrow"/> bigger on every side, like Minecraft's
    /// EntityFishHook. Null if it touches none.
    /// </summary>
    public static (int Id, double Fraction)? FirstHit(Vec3d from, Vec3d to, IReadOnlyList<RodTarget> targets)
    {
        Vec3d move = to - from;
        double length = Math.Sqrt(move.X * move.X + move.Y * move.Y + move.Z * move.Z);
        Vec3d direction = length > 1e-9 ? move * (1 / length) : new Vec3d(0, -1, 0);
        (int Id, double Fraction)? best = null;
        foreach (RodTarget target in targets)
        {
            Box box = Box.ForPlayer(target.Feet, Tuning.PlayerWidth, Tuning.PlayerHeight).Grow(Tuning.BobberHitboxGrow);
            double? distance = box.Contains(from) ? 0 : box.RayEntry(from, direction, length);
            if (distance is double d)
            {
                double fraction = length > 1e-9 ? d / length : 0;
                if (best is null || fraction < best.Value.Fraction)
                {
                    best = (target.Id, fraction);
                }
            }
        }
        return best;
    }

    private void Fly(double time, Vec3d position, Vec3d motion)
    {
        _startTime = time;
        _tick = 0;
        _from = position;
        _motion = motion;
        Landed = false;
        _to = Step(position);
    }

    /// <summary>
    /// One Minecraft tick of flight from <paramref name="position"/>: move with block
    /// collision, then gravity, then drag (stronger while touching the ground or a wall).
    /// Flying into a block stops it dead where its path touches the block.
    /// </summary>
    private Vec3d Step(Vec3d position)
    {
        Vec3d m = _motion;
        Box box = BoxAt(position);
        (double dx, double dy, double dz) = _world.Clip(box, m.X, m.Y, m.Z, _boxes);
        if (!Landed && (dx != m.X || dy != m.Y || dz != m.Z))
        {
            double f = 1;
            f = dx != m.X ? Math.Min(f, dx / m.X) : f;
            f = dy != m.Y ? Math.Min(f, dy / m.Y) : f;
            f = dz != m.Z ? Math.Min(f, dz / m.Z) : f;
            (dx, dy, dz) = _world.Clip(box, m.X * f, m.Y * f, m.Z * f, _boxes);
            _motion = m * Tuning.BobberImpactSpeedKept;
            Landed = true;
            return position + new Vec3d(dx, dy, dz);
        }
        bool onGround = dy != m.Y && m.Y < 0;
        bool againstWall = dx != m.X || dz != m.Z;
        Landed = onGround || againstWall;
        double drag = Landed ? Tuning.BobberGroundDrag : Tuning.BobberDrag;
        _motion = new Vec3d(
            (dx != m.X ? 0 : m.X) * drag,
            ((dy != m.Y ? 0 : m.Y) - Tuning.BobberGravity) * drag,
            (dz != m.Z ? 0 : m.Z) * drag);
        return position + new Vec3d(dx, dy, dz);
    }

    /// <summary>A standard normal random number (Box-Muller), like Java's Random.nextGaussian.</summary>
    private static double Gaussian(Random random)
    {
        double u = 1.0 - random.NextDouble();
        double v = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
    }
}
