using System;
using System.Collections.Generic;

namespace UhcPvp.Core;

/// <summary>
/// Rules the host picks for the whole match: our own twists on 1.8.9, so they can be playtested
/// against plain 1.8 in the same build (pause menu, "Twists"). Every player's game moves its own
/// player and decides its own clicks, so the host sends these to everyone (the
/// <c>RulesUpdate</c> message) and they must be the same everywhere. A new match starts with
/// <see cref="Default"/>, the values playtesting picked; <see cref="Minecraft"/> is plain 1.8.9.
/// </summary>
/// <param name="StrafeSpeed">Faster strafing, see <see cref="Tuning.McStrafeSpeed"/>. 1 is 1.8.9.</param>
/// <param name="StrafeGrip">Snappier strafing, see <see cref="Tuning.McStrafeGrip"/>. 0 is 1.8.9.</param>
/// <param name="WastedClickDelay">Wasted clicks, in seconds, see <see cref="Tuning.McWastedClickDelay"/>. 0 is 1.8.9.</param>
/// <param name="Pushing">Player pushing, see <see cref="Tuning.McPushing"/>. 0 is 1.8.9 (players walk through each other), 1 is solid.</param>
/// <param name="ComboKnockback">Combo knockback, the sprint hit's extra push as a fraction of 1.8.9's, see <see cref="Tuning.McComboKnockback"/>. 1 is 1.8.9.</param>
/// <param name="HeadStart">Head start, how far in the past others are drawn and hit, in seconds, see <see cref="Tuning.McHeadStart"/>. 0.175 is 1.8.9's; 0 draws them as fresh as possible.</param>
/// <param name="RodCooldown">Rod cooldown, in seconds, see <see cref="Tuning.McRodCooldown"/>. 0 is 1.8.9 (every right-click throws).</param>
/// <param name="RodKnockback">Rod knockback, a rod catch's push and lift as a fraction of 1.8's, see <see cref="Tuning.McRodKnockback"/>. 1 is 1.8's; 0 sends none.</param>
public readonly record struct MatchRules(
    double StrafeSpeed,
    double StrafeGrip,
    double WastedClickDelay,
    double Pushing,
    double ComboKnockback,
    double HeadStart,
    double RodCooldown,
    double RodKnockback)
{
    /// <summary>
    /// Plain 1.8.9: every twist at 1.8.9's setting. The baseline movement and combat are measured
    /// against; on the Twists screen, every slider all the way left except the head start and the
    /// rod knockback.
    /// </summary>
    public static MatchRules Minecraft => new(
        Tuning.McStrafeSpeed, Tuning.McStrafeGrip, Tuning.McWastedClickDelay, Tuning.McPushing,
        Tuning.McComboKnockback, Tuning.McHeadStart, Tuning.McRodCooldown, Tuning.McRodKnockback);

    /// <summary>
    /// What a new match starts with: the owner's picks from playing 0.4.8 (2026-10-23), the
    /// snappiest strafing and a 75 ms head start, and the rod's 0.75 s cooldown from before it
    /// was a twist; everything else 1.8.9's. Also what a missing or broken value from the
    /// settings file or the network becomes.
    /// </summary>
    public static MatchRules Default => new(
        Tuning.DefaultStrafeSpeed, Tuning.DefaultStrafeGrip, Tuning.DefaultWastedClickDelay, Tuning.DefaultPushing,
        Tuning.DefaultComboKnockback, Tuning.DefaultHeadStart, Tuning.DefaultRodCooldown, Tuning.DefaultRodKnockback);

    /// <summary>
    /// The same rules made valid, for values from a file or the network: each is kept within its
    /// slider's range and rounded to its steps; anything that isn't a number becomes the default.
    /// </summary>
    public MatchRules Normalized() => new(
        Snap(StrafeSpeed, Tuning.McStrafeSpeed, Tuning.MaxStrafeSpeed, Tuning.StrafeSpeedStep, Tuning.DefaultStrafeSpeed),
        Snap(StrafeGrip, 0.0, Tuning.MaxStrafeGrip, Tuning.StrafeGripStep, Tuning.DefaultStrafeGrip),
        Snap(WastedClickDelay, 0.0, Tuning.MaxWastedClickDelay, Tuning.WastedClickDelayStep, Tuning.DefaultWastedClickDelay),
        Snap(Pushing, 0.0, Tuning.MaxPushing, Tuning.PushingStep, Tuning.DefaultPushing),
        Snap(ComboKnockback, Tuning.MinComboKnockback, Tuning.McComboKnockback, Tuning.ComboKnockbackStep, Tuning.DefaultComboKnockback),
        Snap(HeadStart, 0.0, Tuning.MaxHeadStart, Tuning.HeadStartStep, Tuning.DefaultHeadStart),
        Snap(RodCooldown, 0.0, Tuning.MaxRodCooldown, Tuning.RodCooldownStep, Tuning.DefaultRodCooldown),
        Snap(RodKnockback, 0.0, Tuning.MaxRodKnockback, Tuning.RodKnockbackStep, Tuning.DefaultRodKnockback));

    /// <summary>Whether every twist is at 1.8.9's setting (plain 1.8).</summary>
    public bool IsMinecraft => Normalized() == Minecraft;

    /// <summary>Whether every twist is at its default, as a new match starts.</summary>
    public bool IsDefault => Normalized() == Default;

    /// <summary>
    /// A short line for the pause menu and F3: "Defaults", "Plain 1.8 (all off)", or what differs
    /// from whichever of the two is closer ("Defaults, but head start 175 ms").
    /// </summary>
    public string Summary()
    {
        MatchRules rules = Normalized();
        if (rules == Default)
        {
            return "Defaults";
        }
        if (rules == Minecraft)
        {
            return "Plain 1.8 (all off)";
        }
        List<string> fromDefault = rules.ChangesFrom(Default);
        List<string> fromMinecraft = rules.ChangesFrom(Minecraft);
        return fromMinecraft.Count < fromDefault.Count
            ? "Plain 1.8, but " + string.Join(", ", fromMinecraft)
            : "Defaults, but " + string.Join(", ", fromDefault);
    }

    /// <summary>"130%"</summary>
    public static string Percent(double value) => $"{Math.Round(value * 100):0}%";

    /// <summary>"50%", or "Off" for 0.</summary>
    public static string PercentOrOff(double value) => value <= 0 ? "Off" : Percent(value);

    /// <summary>"0.15 s", or "Off" for 0.</summary>
    public static string Seconds(double value) => value <= 0 ? "Off" : $"{value:0.00} s";

    /// <summary>Each twist that differs from <paramref name="baseline"/>, as "name value", in the Twists screen's order.</summary>
    private List<string> ChangesFrom(MatchRules baseline)
    {
        var parts = new List<string>();
        void Add(string name, double value, double was, string shown)
        {
            if (value != was)
            {
                // No-break spaces keep each one on a line of its own when the summary wraps.
                parts.Add($"{name} {shown.ToLowerInvariant()}".Replace(' ', ' '));
            }
        }
        Add("strafe speed", StrafeSpeed, baseline.StrafeSpeed, Percent(StrafeSpeed));
        Add("snappiness", StrafeGrip, baseline.StrafeGrip, PercentOrOff(StrafeGrip));
        Add("pushing", Pushing, baseline.Pushing, PercentOrOff(Pushing));
        Add("combo knockback", ComboKnockback, baseline.ComboKnockback, Percent(ComboKnockback));
        Add("head start", HeadStart, baseline.HeadStart, Core.HeadStart.Label(HeadStart));
        Add("wasted clicks", WastedClickDelay, baseline.WastedClickDelay, Seconds(WastedClickDelay));
        Add("rod cooldown", RodCooldown, baseline.RodCooldown, Seconds(RodCooldown));
        Add("rod knockback", RodKnockback, baseline.RodKnockback, PercentOrOff(RodKnockback));
        return parts;
    }

    private static double Snap(double value, double min, double max, double step, double fallback)
    {
        double v = double.IsFinite(value) ? value : fallback;
        v = Math.Clamp(v, min, max);
        v = Math.Round(v / step) * step;
        // Rounding can land a hair off the step; snap so the defaults and 1.8.9's really are them.
        return Math.Round(v, 6);
    }
}
