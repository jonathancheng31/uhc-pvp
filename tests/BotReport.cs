using System;
using System.Globalization;
using Godot;
using UhcPvp.Core;

namespace UhcPvp.Tests;

/// <summary>
/// Tools for working on the fighting bot, off unless asked for (they do nothing in a normal
/// test run):
///   BOT_REPORT=N tools/run_tests.sh BotReport
///     prints the comparison table in docs/combat.md: N duels per pairing (300 is a good number;
///     it takes about a minute), each skill against the 0.4.6 bot and each other under each
///     twist, how long a w-tapping attacker's combos get on each, and today's decisions against
///     the old bot's with the same aim.
///   BOT_TRACE=SEED [BOT_TRACE_SKILL=easy|normal|hard] [BOT_TRACE_VS=easy|normal|hard]
///   [BOT_TRACE_ATTACKER=1] tools/run_tests.sh BotReport
///     prints one duel tick by tick (against the 0.4.6 bot unless BOT_TRACE_VS is set; with
///     BOT_TRACE_ATTACKER, the scripted w-tapping attacker against the bot).
/// </summary>
public class BotReport : TestCase
{
    private const int Seed = 7;

    private static Func<int, IDuelBrain> Skilled(BotDifficulty difficulty) => seed => new SkilledBrain(difficulty, seed);
    private static Func<int, IDuelBrain> Legacy => seed => new LegacyBotBrain(seed);

    public void TestReport()
    {
        string? env = System.Environment.GetEnvironmentVariable("BOT_REPORT");
        if (!int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int duels) || duels <= 0)
        {
            return;
        }
        var rulesets = new (string Name, MatchRules Rules)[]
        {
            ("1.8.9", MatchRules.Default),
            ("wasted clicks 0.15 s", MatchRules.Default with { WastedClickDelay = 0.15 }),
            ("strafe 130% grip 50%", MatchRules.Default with { StrafeSpeed = 1.3, StrafeGrip = 0.5 }),
        };
        GD.Print("--- How comboable: a scripted attacker that w-taps well, against each bot ---");
        Func<int, IDuelBrain>[] attackers =
        {
            _ => new ComboAttacker(),
            seed => new ComboAttacker(Tuning.NormalBot, seed),
        };
        foreach (Func<int, IDuelBrain> attacker in attackers)
        {
            foreach (BotDifficulty d in Enum.GetValues<BotDifficulty>())
            {
                GD.Print(DuelSeries.Run(attacker, Skilled(d), MatchRules.Default, duels, Seed).ComboSummaryA());
            }
            GD.Print(DuelSeries.Run(attacker, Legacy, MatchRules.Default, duels, Seed).ComboSummaryA());
        }
        GD.Print("--- Decisions alone: today's bot against the old bot's decisions with the same eyes and hands ---");
        foreach (BotDifficulty d in Enum.GetValues<BotDifficulty>())
        {
            string label = $"0.4.6 decisions, {BotSkill.Label(d)} aim";
            GD.Print(DuelSeries.Run(Skilled(d), seed => new LegacyBotBrain(seed, BotSkill.For(d), label), MatchRules.Default, duels, Seed).ToString());
        }
        foreach ((string name, MatchRules rules) in rulesets)
        {
            GD.Print($"--- {name} ---");
            foreach (BotDifficulty d in Enum.GetValues<BotDifficulty>())
            {
                GD.Print(DuelSeries.Run(Skilled(d), Legacy, rules, duels, Seed).ToString());
            }
            GD.Print(DuelSeries.Run(Skilled(BotDifficulty.Normal), Skilled(BotDifficulty.Easy), rules, duels, Seed).ToString());
            GD.Print(DuelSeries.Run(Skilled(BotDifficulty.Hard), Skilled(BotDifficulty.Normal), rules, duels, Seed).ToString());
            GD.Print(DuelSeries.Run(Legacy, Legacy, rules, duels, Seed).ToString());
        }
    }

    public void TestTrace()
    {
        string? env = System.Environment.GetEnvironmentVariable("BOT_TRACE");
        if (!int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
        {
            return;
        }
        BotDifficulty skill = Enum.TryParse(System.Environment.GetEnvironmentVariable("BOT_TRACE_SKILL"), true, out BotDifficulty s)
            ? s : BotDifficulty.Normal;
        IDuelBrain a = new SkilledBrain(skill, 1);
        string? opponent = System.Environment.GetEnvironmentVariable("BOT_TRACE_VS");
        IDuelBrain b = Enum.TryParse(opponent, true, out BotDifficulty vs) ? new SkilledBrain(vs, 2) : new LegacyBotBrain(2);
        if (System.Environment.GetEnvironmentVariable("BOT_TRACE_ATTACKER") is not null)
        {
            (a, b) = (new ComboAttacker(), a);
        }
        DuelResult r = BotDuel.Fight(a, b, MatchRules.Default, seed, 30, line => GD.Print(line));
        GD.Print($"winner {r.Winner} after {r.Seconds:0.0} s");
    }
}
