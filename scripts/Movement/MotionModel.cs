using System;
using UhcPvp.Core;

namespace UhcPvp.Movement;

/// <summary>
/// How one friction setting (ground or air) behaves in our 60-tick simulation.
///
/// Minecraft updates horizontal velocity once per tick: v += accel; move by v; v *= friction.
/// Simply running that three times as often with smaller numbers drifts from Minecraft (25%
/// longer stopping distance, slower starts), because Minecraft applies a whole tick of
/// acceleration up front. So each of our ticks does:
///   p = v + StateGain * accel;  move by p + Feedthrough * accel;  v = SubFriction * p
/// where accel is in Minecraft units (blocks per MC tick per MC tick). The two gains are solved
/// so that, while the friction setting stays the same, the position after every 3 of our ticks
/// is exactly where Minecraft would be after 1 of its ticks, for any input that changes on
/// Minecraft tick boundaries. Our extra ticks only add finer steps in between.
/// </summary>
public readonly record struct FrictionRegime(
    double McFriction,
    double SubFriction,
    double StateGain,
    double Feedthrough,
    double ImpulseToState)
{
    public static FrictionRegime For(double mcFriction)
    {
        int n = Tuning.SubticksPerMcTick;
        double f = mcFriction;
        double fs = Math.Pow(f, 1.0 / n);
        double b = f * (1 - fs) * (1 - fs) / (fs * (1 - f) * (1 - f));
        double c = 1.0 / (n * (1 - f)) - b / (1 - fs);
        // A velocity u (blocks per MC tick) at the start of an MC tick moves the player u during
        // that tick. In our state units that's u / (1 + fs + fs^2) per subtick.
        return new FrictionRegime(f, fs, b, c, (1 - fs) / (1 - f));
    }
}

/// <summary>Converts Minecraft 1.8.9's 20-tick movement numbers into our 60-tick ones.</summary>
public static class MotionModel
{
    public static readonly int N = Tuning.SubticksPerMcTick;

    /// <summary>Friction while standing on a block (0.6 slipperiness * 0.91).</summary>
    public static readonly FrictionRegime Ground = FrictionRegime.For(Tuning.BlockSlipperiness * Tuning.AirFriction);
    public static readonly FrictionRegime Air = FrictionRegime.For(Tuning.AirFriction);

    /// <summary>
    /// Vertical motion uses the same scheme. Minecraft's vertical update is "move by v, then
    /// v = (v - gravity) * drag", which is the horizontal form with friction = drag and a constant
    /// acceleration of -gravity * drag, shifted by one input. So gravity becomes
    /// <see cref="GravityInput"/> and our vertical state relates to Minecraft's motionY through
    /// <see cref="VerticalStateFromMc"/>. Jumps, landings and falls then match Minecraft exactly
    /// at every Minecraft tick.
    /// </summary>
    public static readonly FrictionRegime Vertical = FrictionRegime.For(Tuning.VerticalDrag);

    /// <summary>Gravity as a per-tick acceleration in Minecraft units.</summary>
    public static readonly double GravityInput = -Tuning.Gravity * Tuning.VerticalDrag;

    /// <summary>Our vertical state for a Minecraft motionY (blocks per MC tick) at the start of a tick.</summary>
    public static double VerticalStateFromMc(double motionY) => (motionY - GravityInput) * Vertical.ImpulseToState;

    /// <summary>Minecraft's motionY for our vertical state at the start of a tick.</summary>
    public static double McVerticalFromState(double state) => state / Vertical.ImpulseToState + GravityInput;

    /// <summary>Ground acceleration in Minecraft units for a given speed attribute.</summary>
    public static double GroundAcceleration(double speedAttribute)
    {
        double f = Tuning.BlockSlipperiness * Tuning.AirFriction;
        return speedAttribute * (Tuning.GroundAccelerationConstant / (f * f * f));
    }
}
