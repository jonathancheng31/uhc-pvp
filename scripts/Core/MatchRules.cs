using System;

namespace UhcPvp.Core;

/// <summary>
/// Rules the host picks for the whole match: our own twists on 1.8.9, so they can be playtested
/// against plain 1.8 in the same build. Every player's game moves its own player, so the host
/// sends these to everyone (the <c>Rules</c> message) and they must be the same everywhere.
/// </summary>
/// <param name="StrafeSpeed">Faster strafing, see <see cref="Tuning.DefaultStrafeSpeed"/>. 1.0 is 1.8.9.</param>
public readonly record struct MatchRules(double StrafeSpeed)
{
    /// <summary>Plain 1.8.9.</summary>
    public static MatchRules Default => new(Tuning.DefaultStrafeSpeed);

    /// <summary>
    /// The same rules made valid, for values from a file or the network: strafe speed is kept
    /// between 1 and the maximum and rounded to the slider's steps; anything that isn't a number
    /// becomes the default.
    /// </summary>
    public MatchRules Normalized()
    {
        double strafe = double.IsFinite(StrafeSpeed) ? StrafeSpeed : Tuning.DefaultStrafeSpeed;
        strafe = Math.Clamp(strafe, 1.0, Tuning.MaxStrafeSpeed);
        strafe = Math.Round(strafe / Tuning.StrafeSpeedStep) * Tuning.StrafeSpeedStep;
        // Rounding can land a hair off the step; snap so 1.0 really is 1.8.9.
        return new MatchRules(Math.Round(strafe, 6));
    }

    /// <summary>"130%"</summary>
    public static string Percent(double value) => $"{Math.Round(value * 100):0}%";
}
