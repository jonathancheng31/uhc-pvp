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
///
/// It also disappears on its own: <see cref="Tuning.BobberGroundSeconds"/> after it comes to
/// rest, or <see cref="Tuning.BobberHookSeconds"/> after it catches someone. Both follow from
/// the flight and the time of the catch, which every game knows, so every game makes it vanish
/// at the same moment of the flight it shows.
/// </summary>
public sealed class Bobber
{
    private readonly ICollisionWorld _world;
    private readonly List<Box> _boxes = new();
    /// <summary>Host time the current flight started (the cast, or a release).</summary>
    private double _startTime;
    /// <summary>
    /// Where it is at the start of each Minecraft tick of the flight worked out so far, the last
    /// being where the latest tick ends. All kept, so any time gives the same answer in any
    /// order: a host bot's bobber is checked for catches ahead of where it's drawn.
    /// </summary>
    private readonly List<Vec3d> _path = new();
    /// <summary>Motion for the next tick to work out, in blocks per MC tick.</summary>
    private Vec3d _motion;
    /// <summary>The tick it came to rest in (see <see cref="RestingSince"/>), as of the latest tick worked out.</summary>
    private int? _restTick;
    /// <summary>
    /// When it had lain still for <see cref="Tuning.BobberGroundSeconds"/>, once the flight's been
    /// worked out that far. It stays put from then on, whatever's asked later.
    /// </summary>
    private double? _restOver;

    public Bobber(ICollisionWorld world, int throwerId, double castTime, Vec3d origin, Vec3d velocity)
    {
        _world = world;
        ThrowerId = throwerId;
        CastTime = castTime;
        Origin = origin;
        Fly(castTime, origin, velocity);
    }

    public int ThrowerId { get; }
    /// <summary>Host time of the cast.</summary>
    public double CastTime { get; }
    /// <summary>Where the cast started.</summary>
    public Vec3d Origin { get; }
    /// <summary>The player it's caught on, if any.</summary>
    public int? HookedId { get; private set; }
    /// <summary>
    /// Host time it caught <see cref="HookedId"/>: when its path reached them, which is also when
    /// every game shows it hanging on them.
    /// </summary>
    public double? HookedAt { get; private set; }
    /// <summary>Whether it's lying on the ground (or against a wall), as of the latest tick worked out.</summary>
    public bool Landed { get; private set; }
    /// <summary>
    /// Host time this flight first touched the ground or a wall (where it's drawn then, so its
    /// sound plays as it's seen to land), or null while it's still in the air. Worked out up to
    /// one Minecraft tick ahead of the furthest time asked for.
    /// </summary>
    public double? LandedAt { get; private set; }
    /// <summary>
    /// Host time it came to rest, or null while it's moving, as of the latest tick worked out.
    /// At rest means it has hardly moved (less than <see cref="Tuning.BobberRestSpeed"/>) in every
    /// Minecraft tick since. Touching down isn't enough: it slides a little on the ground first,
    /// and against a wall or a ceiling it only touches for a tick or two before it falls. Gravity
    /// pulls a bobber in the air faster than that within a tick, so it can only stay at rest
    /// while something holds it up: the floor, the top of a wall, anything it ends up lying on.
    /// </summary>
    public double? RestingSince => _restTick is int tick ? _startTime + tick / (double)Tuning.McTickRate : null;

    /// <summary>
    /// Host time it disappears on its own: <see cref="Tuning.BobberHookSeconds"/> after catching
    /// someone, or <see cref="Tuning.BobberGroundSeconds"/> after coming to rest. Null while it's
    /// flying (as of the latest tick worked out).
    /// </summary>
    public double? GoneAt => HookedAt is double hookedAt
        ? hookedAt + Tuning.BobberHookSeconds
        : _restOver ?? RestingSince + Tuning.BobberGroundSeconds;

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
    /// Where the flying bobber is at host time <paramref name="time"/>, along the straight line
    /// of the Minecraft tick it's in. Times before the flight started give its start. Works out
    /// new ticks as needed; times already passed give where it was then.
    /// </summary>
    public Vec3d PositionAt(double time)
    {
        double ticks = (time - _startTime) * Tuning.McTickRate;
        if (!(ticks > 0))
        {
            return _path[0];
        }
        int tick = (int)Math.Floor(ticks);
        while (_path.Count <= tick + 1)
        {
            WorkOutTick();
        }
        Vec3d start = _path[tick];
        return start + (_path[tick + 1] - start) * (ticks - tick);
    }

    /// <summary>Caught on a player at host time <paramref name="time"/>: it hangs on them from then on.</summary>
    public void Hook(int playerId, double time)
    {
        HookedId = playerId;
        HookedAt = time;
    }

    /// <summary>
    /// The caught player is gone (died or left): the bobber drops from where it hung, starting
    /// at host time <paramref name="time"/>. Once it lands, it's the time at rest that counts.
    /// </summary>
    public void Release(double time, Vec3d position)
    {
        HookedId = null;
        HookedAt = null;
        Fly(time, position, Vec3d.Zero);
    }

    /// <summary>
    /// Whether it has disappeared on its own by host time <paramref name="time"/> (see
    /// <see cref="GoneAt"/>). Works out the flight up to then, like <see cref="PositionAt"/>.
    /// </summary>
    public bool IsGoneAt(double time)
    {
        if (HookedId is null)
        {
            PositionAt(time);
        }
        return GoneAt is double goneAt && time >= goneAt;
    }

    /// <summary>
    /// Whom it catches moving from where it is at host time <paramref name="from"/> to where it
    /// is at <paramref name="to"/> (a straight line, so for short steps: the game checks every
    /// 1/60 s), and when: the first of <paramref name="targets"/> it touches (see
    /// <see cref="FirstHit"/>). Once it's gone it catches no one, so only the time until then
    /// counts. Null if it touches no one, or it already hangs on someone.
    /// </summary>
    public (int Id, double Time)? Catch(double from, double to, IReadOnlyList<RodTarget> targets)
    {
        if (HookedId is not null)
        {
            return null;
        }
        double until = IsGoneAt(to) ? Math.Min(to, GoneAt ?? to) : to;
        if (until <= from || FirstHit(PositionAt(from), PositionAt(until), targets) is not { } hit)
        {
            return null;
        }
        return (hit.Id, from + (until - from) * hit.Fraction);
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
        _path.Clear();
        _path.Add(position);
        _motion = motion;
        Landed = false;
        LandedAt = null;
        _restTick = null;
        _restOver = null;
        WorkOutTick();
    }

    /// <summary>Works out the next Minecraft tick of the flight, from where the latest one ends.</summary>
    private void WorkOutTick()
    {
        _path.Add(Step(_path[^1]));
        NoteLanding();
        NoteRest();
    }

    /// <summary>The tick just worked out ends at the end of <see cref="_path"/>: if it touched down, that's when.</summary>
    private void NoteLanding()
    {
        if (Landed && LandedAt is null)
        {
            LandedAt = _startTime + (_path.Count - 1) / (double)Tuning.McTickRate;
        }
    }

    /// <summary>
    /// The tick just worked out: if it hardly moved, it's at rest from the start of that tick
    /// (unless it already was); if it moved, it isn't. Once it has lain still for the ground
    /// time, that moment is kept.
    /// </summary>
    private void NoteRest()
    {
        int tick = _path.Count - 2;
        if ((_path[^1] - _path[^2]).Length >= Tuning.BobberRestSpeed)
        {
            _restTick = null;
            return;
        }
        _restTick ??= tick;
        if (_restOver is null && tick + 1 - _restTick.Value >= Tuning.BobberGroundSeconds * Tuning.McTickRate - 1e-9)
        {
            _restOver = RestingSince + Tuning.BobberGroundSeconds;
        }
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
