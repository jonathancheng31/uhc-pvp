using System;
using UhcPvp.Movement;

namespace UhcPvp.Core;

/// <summary>
/// What a practice bot sees of another player, the way a person does: where they were a
/// reaction time ago, carried forward by how they were moving then. A player who keeps moving
/// the same way is followed well; a sudden change of direction (a strafe switch, knockback)
/// only shows up a reaction time later, so good strafing makes the bot's aim and timing slip.
/// </summary>
public sealed class BotPerception
{
    /// <summary>Seconds of history kept: enough for the slowest reaction plus the motion window.</summary>
    private const double HistorySeconds = 0.6;
    /// <summary>A jump farther than this between two looks is a teleport (respawn): start over.</summary>
    private const double TeleportDistance = 4.0;

    private readonly (double Time, Vec3d Feet)[] _samples = new (double, Vec3d)[64];
    private int _start;
    private int _count;

    public bool IsEmpty => _count == 0;

    /// <summary>The player was at <paramref name="feet"/> at <paramref name="time"/> (seconds, rising).</summary>
    public void Observe(double time, Vec3d feet)
    {
        if (_count > 0)
        {
            (double lastTime, Vec3d lastFeet) = Sample(_count - 1);
            if (time <= lastTime)
            {
                return;
            }
            if ((feet - lastFeet).Length > TeleportDistance)
            {
                _count = 0;
            }
        }
        if (_count == _samples.Length)
        {
            _start = (_start + 1) % _samples.Length;
            _count--;
        }
        _samples[(_start + _count) % _samples.Length] = (time, feet);
        _count++;
        // Drop what's too old to matter.
        while (_count > 2 && Sample(1).Time < time - HistorySeconds)
        {
            _start = (_start + 1) % _samples.Length;
            _count--;
        }
    }

    /// <summary>Where the player was at <paramref name="time"/>, between the looks around it (the oldest or newest outside them).</summary>
    public Vec3d PositionAt(double time)
    {
        if (_count == 0)
        {
            return Vec3d.Zero;
        }
        if (time <= Sample(0).Time)
        {
            return Sample(0).Feet;
        }
        for (int i = 1; i < _count; i++)
        {
            (double t1, Vec3d p1) = Sample(i);
            if (time <= t1)
            {
                (double t0, Vec3d p0) = Sample(i - 1);
                return p0 + (p1 - p0) * ((time - t0) / (t1 - t0));
            }
        }
        return Sample(_count - 1).Feet;
    }

    /// <summary>How the player was moving at <paramref name="time"/> (blocks per second), over the <paramref name="window"/> before it.</summary>
    public Vec3d VelocityAt(double time, double window)
    {
        if (_count < 2)
        {
            return Vec3d.Zero;
        }
        double from = Math.Max(time - window, Sample(0).Time);
        double to = Math.Min(time, Sample(_count - 1).Time);
        if (to - from < 1e-6)
        {
            return Vec3d.Zero;
        }
        return (PositionAt(to) - PositionAt(from)) * (1 / (to - from));
    }

    /// <summary>
    /// Where the bot believes the player is at <paramref name="now"/>: where they were
    /// <paramref name="reaction"/> seconds ago, moved on by <paramref name="anticipation"/> of
    /// how far they'd have gone since at the speed they had then.
    /// </summary>
    public Vec3d Perceived(double now, double reaction, double anticipation, double window)
    {
        double seen = now - reaction;
        Vec3d velocity = VelocityAt(seen, window);
        return PositionAt(seen) + velocity.WithY(velocity.Y * 0.5) * (reaction * anticipation);
    }

    private (double Time, Vec3d Feet) Sample(int index) => _samples[(_start + index) % _samples.Length];
}
