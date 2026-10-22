using System;

namespace UhcPvp.Core;

/// <summary>
/// Rules the host picks for the whole match: our own twists on 1.8.9, so they can be playtested
/// against plain 1.8 in the same build (pause menu, "Twists"). Every player's game moves its own
/// player and decides its own clicks, so the host sends these to everyone (the
/// <c>RulesUpdate</c> message) and they must be the same everywhere. The defaults are 1.8.9.
/// </summary>
/// <param name="StrafeSpeed">Faster strafing, see <see cref="Tuning.DefaultStrafeSpeed"/>. 1 is 1.8.9.</param>
/// <param name="StrafeGrip">Snappier strafing, see <see cref="Tuning.DefaultStrafeGrip"/>. 0 is 1.8.9.</param>
/// <param name="WastedClickDelay">Wasted clicks, in seconds, see <see cref="Tuning.DefaultWastedClickDelay"/>. 0 is 1.8.9.</param>
public readonly record struct MatchRules(
    double StrafeSpeed = Tuning.DefaultStrafeSpeed,
    double StrafeGrip = Tuning.DefaultStrafeGrip,
    double WastedClickDelay = Tuning.DefaultWastedClickDelay)
{
    /// <summary>Plain 1.8.9.</summary>
    public static MatchRules Default => new(Tuning.DefaultStrafeSpeed, Tuning.DefaultStrafeGrip, Tuning.DefaultWastedClickDelay);

    /// <summary>
    /// The same rules made valid, for values from a file or the network: each is kept within its
    /// slider's range and rounded to its steps; anything that isn't a number becomes the default.
    /// </summary>
    public MatchRules Normalized() => new(
        Snap(StrafeSpeed, 1.0, Tuning.MaxStrafeSpeed, Tuning.StrafeSpeedStep, Tuning.DefaultStrafeSpeed),
        Snap(StrafeGrip, 0.0, Tuning.MaxStrafeGrip, Tuning.StrafeGripStep, Tuning.DefaultStrafeGrip),
        Snap(WastedClickDelay, 0.0, Tuning.MaxWastedClickDelay, Tuning.WastedClickDelayStep, Tuning.DefaultWastedClickDelay));

    /// <summary>Whether every twist is at 1.8.9's setting.</summary>
    public bool IsMinecraft => Normalized() == Default;

    /// <summary>"130%"</summary>
    public static string Percent(double value) => $"{Math.Round(value * 100):0}%";

    /// <summary>"0.15 s", or "Off" for 0.</summary>
    public static string Seconds(double value) => value <= 0 ? "Off" : $"{value:0.00} s";

    private static double Snap(double value, double min, double max, double step, double fallback)
    {
        double v = double.IsFinite(value) ? value : fallback;
        v = Math.Clamp(v, min, max);
        v = Math.Round(v / step) * step;
        // Rounding can land a hair off the step; snap so the defaults really are 1.8.9.
        return Math.Round(v, 6);
    }
}
