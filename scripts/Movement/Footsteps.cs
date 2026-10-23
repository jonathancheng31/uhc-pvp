using System;

namespace UhcPvp.Movement;

/// <summary>What a player's feet did this frame: a footstep, and/or a landing from a fall this many blocks high (0 for none).</summary>
public readonly record struct FootstepEvent(bool Step, double LandedFrom)
{
    public static readonly FootstepEvent None = new(false, 0);
}

/// <summary>
/// Turns a player's movement, as drawn on this screen, into footsteps and landings, so they
/// sound exactly when the player is seen to move. Works the same for every player: you, bots
/// and other people (from where their smoothed position is drawn).
///
/// A step plays every <see cref="Stride"/> blocks walked on the ground, like Minecraft's step
/// sounds (sneaking is silent, as in Minecraft). Touching down after falling at least
/// <see cref="MinFall"/> blocks plays a landing instead of a step, and the stride starts over.
/// </summary>
public sealed class Footsteps
{
    /// <summary>
    /// Blocks between footsteps. A sprinting player model plants a foot about every 1.3 blocks;
    /// Minecraft plays a step every 1.67. In between: steps that match the legs closely enough,
    /// without a constant patter.
    /// </summary>
    public const double Stride = 1.4;
    /// <summary>Starting to move from standing still, the first step comes after this many blocks.</summary>
    public const double FirstStep = 0.45;
    /// <summary>Standing still this long (seconds) counts as stopped: the next start gets a quick first step.</summary>
    public const double StopTime = 0.2;
    /// <summary>Falls lower than this (blocks) land silently, so bumps and jitter make no sound.</summary>
    public const double MinFall = 0.25;
    /// <summary>Moving further than this in one frame is a respawn or teleport, not walking.</summary>
    public const double TeleportDistance = 3.0;

    private Vec3d? _last;
    private bool _grounded;
    private double _top;
    private double _walked;
    private double _still;

    /// <summary>
    /// Call every frame with where the player is drawn (<paramref name="feet"/>) and whether
    /// they're resting on something there (<paramref name="grounded"/>).
    /// </summary>
    public FootstepEvent Update(Vec3d feet, bool grounded, bool sneaking, bool dead, double delta)
    {
        if (dead || _last is not Vec3d last || (feet - last).Length > TeleportDistance)
        {
            _last = dead ? null : feet;
            _grounded = grounded;
            _top = feet.Y;
            _walked = Stride - FirstStep;
            _still = 0;
            return FootstepEvent.None;
        }
        Vec3d move = feet - last;
        _last = feet;

        double landedFrom = 0;
        if (!grounded)
        {
            _top = _grounded ? feet.Y : Math.Max(_top, feet.Y);
        }
        else if (!_grounded && _top - feet.Y >= MinFall)
        {
            landedFrom = _top - feet.Y;
            _walked = 0;
        }
        _grounded = grounded;
        if (!grounded || landedFrom > 0)
        {
            return new FootstepEvent(false, landedFrom);
        }

        double horizontal = Math.Sqrt(move.X * move.X + move.Z * move.Z);
        if (horizontal < 1e-4)
        {
            _still += delta;
            if (_still >= StopTime)
            {
                _walked = Math.Max(_walked, Stride - FirstStep);
            }
            return FootstepEvent.None;
        }
        _still = 0;
        if (sneaking)
        {
            return FootstepEvent.None;
        }
        _walked += horizontal;
        if (_walked < Stride)
        {
            return FootstepEvent.None;
        }
        _walked = Math.Min(_walked - Stride, Stride);
        return new FootstepEvent(true, 0);
    }
}
