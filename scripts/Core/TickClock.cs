using System;

namespace UhcPvp.Core;

/// <summary>
/// Host-clock times for the game's ticks, to stamp the states it records and sends. Reading the
/// clock during a tick isn't enough: when a frame runs late, the engine runs two or three ticks
/// back to back, and their states would get nearly the same time and then a gap, so others would
/// see that player stutter. These times are one tick apart, pulled gently toward the clock (and
/// jumping to it after a stall or a clock correction).
/// </summary>
public sealed class TickClock
{
    /// <summary>How much of the gap to the clock each tick closes.</summary>
    public const double Pull = 0.1;
    /// <summary>Beyond this gap (seconds) the time jumps straight to the clock.</summary>
    public const double MaxGap = 0.1;

    private readonly double _step;
    private bool _started;

    public TickClock(double step) => _step = step;

    /// <summary>The time of the latest tick.</summary>
    public double Time { get; private set; }

    /// <summary>A tick runs and the clock reads <paramref name="now"/>; returns this tick's time.</summary>
    public double Tick(double now)
    {
        double next = Time + _step;
        Time = !_started || Math.Abs(now - next) > MaxGap ? now : next + (now - next) * Pull;
        _started = true;
        return Time;
    }
}
