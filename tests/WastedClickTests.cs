using System;
using System.Collections.Generic;
using System.Linq;
using UhcPvp.Combat;
using UhcPvp.Core;

namespace UhcPvp.Tests;

/// <summary>
/// The wasted-clicks twist (a match rule, not 1.8.9): a click that does nothing blocks clicking
/// for a moment, which takes the edge away from spam clicking.
/// </summary>
public class WastedClickTests : TestCase
{
    public void TestOffIsMinecraft()
    {
        var lockout = new ClickLockout();
        lockout.Wasted(0);
        Check(!lockout.Blocks, "a delay of 0 never blocks a click");
        CheckEqual(MatchRules.Minecraft.WastedClickDelay, 0.0, "off in plain 1.8.9");
        CheckEqual(MatchRules.Default.WastedClickDelay, 0.0, "and by default");
    }

    public void TestLockoutLastsTheDelay()
    {
        var lockout = new ClickLockout();
        lockout.Wasted(0.15);
        int blockedTicks = 0;
        while (lockout.Blocks)
        {
            blockedTicks++;
            lockout.Tick();
        }
        CheckEqual(blockedTicks, 9, "0.15 s is 9 of our ticks");
        lockout.Wasted(0.15);
        CheckNear(lockout.FractionLeft, 1, 1e-12, "full right after a wasted click");
        lockout.Reset();
        Check(!lockout.Blocks, "a respawn clears it");
    }

    public void TestWhatCountsAsWasted()
    {
        Check(ClickLockout.IsWasted(null), "hitting nothing, a block or a teammate");
        Check(ClickLockout.IsWasted(HitResult.Ignored), "hitting someone still in hit protection");
        Check(!ClickLockout.IsWasted(HitResult.Hurt), "a hit that counts");
        Check(!ClickLockout.IsWasted(HitResult.Stronger), "a stronger hit (a crit) inside protection");
    }

    /// <summary>
    /// A clicker with perfect aim on a target in reach, over a minute of hits: how long after each
    /// half second of hit protection ends their next hit lands, on average and at worst (seconds).
    /// <paramref name="clickTimes"/> gives the clicks, from when the last hit landed and when its
    /// protection ends.
    /// </summary>
    private static (double Mean, double Worst) DelayAfterProtection(
        double wastedDelay, Func<double, double, Random, IEnumerable<double>> clickTimes, int seed)
    {
        var random = new Random(seed);
        var protection = new HitProtection();
        var lockout = new ClickLockout();
        var delays = new List<double>();
        double landedAt = 0;
        protection.Hit(8, HitProtection.TickAt(landedAt));
        double protectionEnds = (HitProtection.TickAt(landedAt) + Tuning.HurtImmunityThresholdMcTicks) / (double)Tuning.McTickRate;
        var clicks = new Queue<double>(clickTimes(landedAt, protectionEnds, random));
        for (int s = 1; s < 60 * Tuning.SimRate; s++)
        {
            double now = s / (double)Tuning.SimRate;
            lockout.Tick();
            while (clicks.Count > 0 && clicks.Peek() <= now)
            {
                double click = clicks.Dequeue();
                if (lockout.Blocks)
                {
                    continue;
                }
                HitResult result = protection.Hit(8, HitProtection.TickAt(click)).Result;
                if (ClickLockout.IsWasted(result))
                {
                    lockout.Wasted(wastedDelay);
                    continue;
                }
                delays.Add(click - protectionEnds);
                landedAt = click;
                protectionEnds = (HitProtection.TickAt(landedAt) + Tuning.HurtImmunityThresholdMcTicks) / (double)Tuning.McTickRate;
                clicks = new Queue<double>(clickTimes(landedAt, protectionEnds, random));
                break;
            }
        }
        return (delays.Average(), delays.Max());
    }

    /// <summary>Clicking steadily at <paramref name="cps"/> clicks a second, from a random moment.</summary>
    private static Func<double, double, Random, IEnumerable<double>> Steady(double cps) => (landed, _, random) =>
        Enumerable.Range(1, 200).Select(i => landed + (i - random.NextDouble()) / cps);

    /// <summary>One click aimed at the moment protection ends, a little late (0 to 50 ms), then again every 0.2 s.</summary>
    private static IEnumerable<double> Timed(double landed, double ends, Random random) =>
        Enumerable.Range(0, 10).Select(i => ends + random.NextDouble() * 0.05 + i * 0.2);

    public void TestSpamLosesItsEdgeToTiming()
    {
        var (spamOff, _) = DelayAfterProtection(0, Steady(15), 1);
        var (spam, spamWorst) = DelayAfterProtection(0.15, Steady(15), 2);
        var (timed, _) = DelayAfterProtection(0.15, Timed, 3);
        // In 1.8.9, 15 clicks a second lands within a fifteenth of a second of protection ending.
        Check(spamOff < 0.07, $"spam without the twist: {spamOff * 1000:0} ms after protection");
        // With the twist, clicks into protection lock you out, so spam lands later...
        Check(spam > spamOff + 0.03, $"spam with 0.15 s: {spam * 1000:0} ms (was {spamOff * 1000:0})");
        // ...and isn't a guaranteed perfectly timed hit...
        Check(spamWorst > 0.1, $"spam's worst: {spamWorst * 1000:0} ms");
        // ...while one well-timed click beats it.
        Check(timed < spam, $"a timed click: {timed * 1000:0} ms, spam {spam * 1000:0} ms");
    }

    public void TestClickingFasterGainsLittle()
    {
        // In 1.8.9, 15 clicks a second lands its next hit much sooner than 8 a second does; with
        // the twist at 0.15 s the difference nearly goes away.
        double Gap(double delay) =>
            DelayAfterProtection(delay, Steady(8), 5).Mean - DelayAfterProtection(delay, Steady(15), 5).Mean;
        Check(Gap(0) > 0.02, $"1.8.9: 15 vs 8 clicks a second gains {Gap(0) * 1000:0} ms");
        Check(Gap(0.15) < 0.015, $"0.15 s: 15 vs 8 clicks a second gains only {Gap(0.15) * 1000:0} ms");
    }
}
