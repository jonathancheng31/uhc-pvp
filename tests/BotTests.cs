using System;
using System.Collections.Generic;
using Godot;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Gameplay;
using UhcPvp.Movement;

namespace UhcPvp.Tests;

/// <summary>
/// The fighting practice bot: what it sees and how it aims (like a person, not an aimbot), the
/// ways to escape a combo and when it uses them, and whole duels between bots (tests/BotDuel.cs)
/// to check that each skill beats the one below it, that it stands up to the 0.4.6 bot, and
/// that Easy can be comboed. tests/BotReport.cs prints the full comparison.
/// </summary>
public class BotTests : TestCase
{
    private const double Tick = 1.0 / Tuning.SimRate;
    private const int FoeId = 7;
    private static readonly string[] EasyArgs = { "--bot-skill=EASY" };

    private static Func<int, IDuelBrain> Skilled(BotDifficulty difficulty) => seed => new SkilledBrain(difficulty, seed);

    // --- Seeing and aiming -------------------------------------------------------------------

    public void TestPerceptionIsLateButLeads()
    {
        var seen = new BotPerception();
        // Someone walking along X at 4 blocks a second for a second...
        for (int i = 0; i <= 60; i++)
        {
            seen.Observe(i * Tick, new Vec3d(4 * i * Tick, 0, 0));
        }
        CheckNear(seen.Perceived(1, 0.15, 0, Tuning.BotMotionWindow).X, 3.4, 1e-6, "without leading, it sees where they were 0.15 s ago");
        CheckNear(seen.Perceived(1, 0.15, 1, Tuning.BotMotionWindow).X, 4.0, 1e-6, "leading fully, steady movement is seen where it is");
        // ...then turning back.
        for (int i = 1; i <= 30; i++)
        {
            seen.Observe(1 + i * Tick, new Vec3d(4 - 4 * i * Tick, 0, 0));
        }
        CheckNear(seen.Perceived(1.1, 0.15, 1, Tuning.BotMotionWindow).X, 4.4, 1e-6,
            "0.1 s after the turn it still believes they carry on (they're at 3.6): good strafing fools it");
        CheckNear(seen.Perceived(1.5, 0.15, 1, Tuning.BotMotionWindow).X, 2.0, 1e-6, "a reaction time later it has caught up");
        seen.Observe(1.6, new Vec3d(30, 0, 0));
        CheckNear(seen.Perceived(1.7, 0.15, 1, Tuning.BotMotionWindow).X, 30, 1e-6, "a respawn starts it over");
    }

    public void TestAimTurnsLikeAHand()
    {
        var aim = new BotAim();
        var random = new Random(1);
        BotSkill skill = Tuning.HardBot with { AimErrorDegrees = 0 };
        double yaw = 0;
        double pitch = 0;
        double fastest = 0;
        for (int i = 0; i < 60; i++)
        {
            (double y, double p) = aim.Step(yaw, pitch, 120, -20, skill, Tick, random);
            fastest = Math.Max(fastest, Math.Abs(BotAim.WrapDegrees(y - yaw)));
            (yaw, pitch) = (y, p);
        }
        Check(fastest <= skill.TurnSpeed * Tick + 1e-9, "never turns faster than its top speed");
        CheckNear(yaw, 120, 0.5, "a second later it's on target (yaw)");
        CheckNear(pitch, -20, 0.5, "and pitch");
    }

    public void TestAimFollowsAMovingTarget()
    {
        var aim = new BotAim();
        var random = new Random(2);
        BotSkill skill = Tuning.NormalBot with { AimErrorDegrees = 0 };
        double yaw = 0;
        double target = 0;
        for (int i = 0; i < 120; i++)
        {
            target += 60 * Tick; // crossing its view at 60 degrees a second
            (yaw, _) = aim.Step(yaw, 0, target, 0, skill, Tick, random);
        }
        CheckNear(yaw, target, 0.5, "tracks smoothly, like a person following someone with their eyes");
    }

    public void TestAimWandersAsMuchAsItsSkill()
    {
        foreach (BotSkill skill in new[] { Tuning.EasyBot, Tuning.HardBot })
        {
            var aim = new BotAim();
            var random = new Random(3);
            double yaw = 0;
            double squares = 0;
            int n = 60 * Tuning.SimRate;
            for (int i = 0; i < n; i++)
            {
                (yaw, _) = aim.Step(yaw, 0, 0, 0, skill, Tick, random);
                squares += yaw * yaw;
            }
            double typical = Math.Sqrt(squares / n);
            Check(typical > skill.AimErrorDegrees * 0.6 && typical < skill.AimErrorDegrees * 1.2,
                $"its aim wanders about {skill.AimErrorDegrees} degrees off a still target (got {typical:0.00})");
        }
    }

    // --- Escaping combos ---------------------------------------------------------------------

    /// <summary>
    /// The ways to escape a combo, measured: how far a sprint hit sends someone who was running
    /// at the attacker, over the half second before the attacker's next hit can land. Holding W
    /// steers against it in the air; clicking the attacker right back while sprinting slows you
    /// to 60% (1.8.9's attack slowdown, <see cref="PlayerMotor.OnHitLanded"/>), knockback
    /// included (hit selecting); a sprint-jump on the very tick the knockback arrives pushes you
    /// back toward the attacker (jump reset), while a tick late you're already in the air.
    /// The bot uses each by choice, as often as its skill says (docs/combat.md).
    /// </summary>
    public void TestWaysToEscapeACombo()
    {
        double carried = Flight(holdForward: false, jumpAt: null);
        double held = Flight(holdForward: true, jumpAt: null);
        double click = Flight(holdForward: true, jumpAt: null, 1);
        double twoClicks = Flight(holdForward: true, jumpAt: null, 1, 4);
        double lateClick = Flight(holdForward: true, jumpAt: null, 12);
        double jumpReset = Flight(holdForward: true, jumpAt: 1);
        double jumpLate = Flight(holdForward: true, jumpAt: 1 + Tuning.SubticksPerMcTick);
        double jumpEarly = Flight(holdForward: true, jumpAt: 1 - Tuning.SubticksPerMcTick);
        if (System.Environment.GetEnvironmentVariable("BOT_REPORT") is not null)
        {
            GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Knockback flight in 0.5 s: carried {carried:0.00}, W held {held:0.00}, + one click {click:0.00}, "
                + $"+ two clicks {twoClicks:0.00}, + a click 0.2 s late {lateClick:0.00}, + jump reset {jumpReset:0.00}, "
                + $"jump a tick late {jumpLate:0.00}, a tick early {jumpEarly:0.00}"));
        }
        // Measured: carried 4.02, W held 2.54, + one click 1.14, + two 0.59, jump reset 1.65.
        CheckNear(carried, 4.02, 0.05, "let the knockback carry you");
        CheckNear(held / carried, 0.63, 0.03, "holding W cuts it by over a third");
        CheckNear(click / held, 0.45, 0.03, "one click right back more than halves it again");
        CheckNear(twoClicks / held, 0.23, 0.03, "two clicks: under a quarter");
        Check(lateClick > click && lateClick < held, "a later click cuts less");
        CheckNear(jumpReset / held, 0.65, 0.05, "a jump reset on the exact tick cuts about a third");
        CheckNear(jumpLate, held, 0.01, "a tick late does nothing");
        Check(jumpEarly > jumpReset, "a tick early is worse than on time");
    }

    /// <summary>
    /// A victim running at the attacker takes a sprint hit, then holds W (or lets go), jumps at
    /// the given tick (our 1/60 s ticks after the hit; 1 is the first, negative before the hit)
    /// and clicks the attacker at the given ticks. How far it flies in half a second.
    /// </summary>
    private static double Flight(bool holdForward, int? jumpAt, params int[] clicksAt)
    {
        var world = new UhcPvp.World.VoxelWorld(new Vector3I(64, 24, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, 4, 63), UhcPvp.World.Block.Stone);
        var victim = new PlayerMotor(world, new Vec3d(32, 5, 40));
        // Yaw 0 faces -Z: the victim runs toward an attacker 3 blocks ahead.
        var run = new MovementInput(1, 0, false, true, false, 0);
        var after = new MovementInput(holdForward ? 1 : 0, 0, false, true, false, 0);
        Vec3d start = Vec3d.Zero;
        for (int tick = -59; tick <= Tuning.SimRate / 2; tick++)
        {
            if (tick == 1)
            {
                Vec3d attacker = victim.Position + new Vec3d(0, 0, -3);
                Vec3d motion = CombatRules.Knockback(
                    victim.ServerMotion, attacker, victim.Position, 180, baseKnockback: true, sprintBonus: true, new Random(1));
                victim.SetMcMotion(CombatRules.VelocityPacket(motion));
                start = victim.Position;
            }
            MovementInput input = tick < 1 ? run : after;
            victim.Step(input with { Jump = tick == jumpAt });
            if (Array.IndexOf(clicksAt, tick) >= 0)
            {
                victim.OnHitLanded(accepted: true);
            }
        }
        return (victim.Position - start).HorizontalLength;
    }

    /// <summary>
    /// Right after your hit lands, what the bot does with it: over many hits, how often it
    /// clicks you back (hit select), jumps (jump reset) and lets go of W (lets the knockback
    /// carry it). Easy never escapes; Hard usually does, on time.
    /// </summary>
    public void TestEscapingIsASkill()
    {
        (double easyClicks, double easyJumps, double easyCarried) = FirstTickAfterAHit(BotDifficulty.Easy);
        (double hardClicks, double hardJumps, double hardCarried) = FirstTickAfterAHit(BotDifficulty.Hard);
        CheckNear(easyClicks, 0, 1e-9, "Easy never hit selects");
        CheckNear(easyJumps, 0, 1e-9, "Easy never jump resets");
        Check(easyCarried > 0.8, $"Easy mostly lets the knockback carry it ({easyCarried:P0})");
        BotSkill hard = Tuning.HardBot;
        // On time (the first Minecraft tick) about 68% of the time at Hard's timing.
        Check(hardClicks > hard.HitSelectChance * 0.5 && hardClicks < hard.HitSelectChance * 0.85,
            $"Hard hit selects on the first tick most of the time ({hardClicks:P0})");
        Check(hardJumps > hard.JumpResetChance * 0.5 && hardJumps < hard.JumpResetChance * 0.85,
            $"Hard jump resets on the exact tick often ({hardJumps:P0})");
        Check(hardCarried < 0.2, $"Hard holds W against the knockback ({1 - hardCarried:P0})");
    }

    /// <summary>
    /// 400 times: a bot faces you 2.5 blocks away, on the ground, sprinting, and your hit lands.
    /// What share of the time, on the very next tick, it clicks, jumps and lets go of W.
    /// </summary>
    private static (double Clicks, double Jumps, double Carried) FirstTickAfterAHit(BotDifficulty difficulty)
    {
        int clicks = 0;
        int jumps = 0;
        int carried = 0;
        const int Trials = 400;
        var foe = new List<BotFoe> { new(FoeId, new Vec3d(0, 0, -2.5), 180, double.PositiveInfinity, false) };
        for (int trial = 0; trial < Trials; trial++)
        {
            var brain = new BotBrain(BotMode.Fight, trial, difficulty);
            var self = new BotSelf(Vec3d.Zero, 0, -5, OnGround: true, SprintKnockback: true, CanClick: true, RodReady: false,
                SinceHurt: double.PositiveInfinity);
            for (int i = 0; i < 30; i++)
            {
                BotIntent warm = brain.Think(self, foe, MatchRules.Minecraft, Tick);
                self = self with { Yaw = warm.Input.Yaw, Pitch = warm.Pitch };
            }
            BotIntent intent = brain.Think(self with { SinceHurt = Tick / 2 }, foe, MatchRules.Minecraft, Tick);
            clicks += intent.Attack ? 1 : 0;
            jumps += intent.Input.Jump ? 1 : 0;
            carried += intent.Input.Forward <= 0 ? 1 : 0;
        }
        return ((double)clicks / Trials, (double)jumps / Trials, (double)carried / Trials);
    }

    public void TestWTapsAfterASprintHit()
    {
        var brain = new BotBrain(BotMode.Fight, 1, BotDifficulty.Hard);
        var foe = new List<BotFoe> { new(FoeId, new Vec3d(0, 0, -4), 180, double.PositiveInfinity, false) };
        var self = new BotSelf(Vec3d.Zero, 0, -5, true, true, true, false, double.PositiveInfinity);
        for (int i = 0; i < 10; i++)
        {
            brain.Think(self, foe, MatchRules.Minecraft, Tick);
        }
        brain.OnAttack(FoeId, HitResult.Hurt, sprintKnockback: true);
        Check(brain.Think(self, foe, MatchRules.Minecraft, Tick).Input.Forward <= 0, "lets go of W (or taps S) after a sprint hit");
        bool sprintsAgain = false;
        for (int i = 0; i < 2 * Tuning.SubticksPerMcTick + 1; i++)
        {
            sprintsAgain = brain.Think(self, foe, MatchRules.Minecraft, Tick).Input.Forward > 0;
        }
        Check(sprintsAgain, "and sprints again within two Minecraft ticks, so its next hit knocks back fully");
    }

    public void TestPatientBotWaitsOutProtection()
    {
        var brain = new BotBrain(BotMode.Fight, 1) { Skill = Tuning.HardBot with { Patience = 1, AimErrorDegrees = 0 } };
        var rules = MatchRules.Minecraft with { WastedClickDelay = 0.15 };
        var foe = new List<BotFoe> { new(FoeId, new Vec3d(0, 0, -2.5), 180, double.PositiveInfinity, false) };
        var self = new BotSelf(Vec3d.Zero, 0, -5, true, true, true, false, double.PositiveInfinity);
        for (int i = 0; i < 30; i++)
        {
            BotIntent warm = brain.Think(self, foe, rules, Tick);
            self = self with { Yaw = warm.Input.Yaw, Pitch = warm.Pitch };
        }
        brain.OnAttack(FoeId, HitResult.Hurt, sprintKnockback: false);
        double firstClick = double.NaN;
        for (int i = 1; i <= 60 && double.IsNaN(firstClick); i++)
        {
            foe[0] = foe[0] with { SinceHurt = i * Tick };
            BotIntent intent = brain.Think(self, foe, rules, Tick);
            self = self with { Yaw = intent.Input.Yaw, Pitch = intent.Pitch };
            if (intent.Attack)
            {
                firstClick = i * Tick;
            }
        }
        Check(firstClick > 0.44 && firstClick < 0.6,
            $"with wasted clicks on, its next click comes as your protection ends, not into it (after {firstClick:0.000} s)");
    }

    public void TestSkillIsRememberedAndChosen()
    {
        CheckEqual(Tuning.DefaultBotDifficulty, BotDifficulty.Normal, "Normal unless the host picks another");
        CheckEqual(UhcPvp.Autoload.Settings.ReadBotDifficulty(new ConfigFile()), BotDifficulty.Normal, "no settings file");
        var config = new ConfigFile();
        UhcPvp.Autoload.Settings.WriteBotDifficulty(config, BotDifficulty.Hard);
        CheckEqual(config.GetValue("practice", "bot_skill").AsString(), "hard", "saved by name");
        CheckEqual(UhcPvp.Autoload.Settings.ReadBotDifficulty(config), BotDifficulty.Hard, "read back");
        config.SetValue("practice", "bot_skill", "impossible");
        CheckEqual(UhcPvp.Autoload.Settings.ReadBotDifficulty(config), BotDifficulty.Normal, "anything else is Normal");
        var args = new UhcPvp.Autoload.LaunchArgs();
        args.Parse(EasyArgs);
        CheckEqual(args.BotDifficulty, BotDifficulty.Easy, "--bot-skill on the command line");
        args.Free();
        foreach (BotDifficulty d in Enum.GetValues<BotDifficulty>())
        {
            CheckEqual(new BotBrain(BotMode.Fight, 1, d).Skill, BotSkill.For(d), $"{BotSkill.Label(d)} uses its numbers");
        }
    }

    // --- Whole fights, under plain 1.8.9 (as docs/combat.md measures them) ----------------------

    public void TestEachSkillBeatsTheOneBelow()
    {
        double hardOverNormal = DuelSeries.Run(Skilled(BotDifficulty.Hard), Skilled(BotDifficulty.Normal), MatchRules.Minecraft, 60).WinRateA;
        double normalOverEasy = DuelSeries.Run(Skilled(BotDifficulty.Normal), Skilled(BotDifficulty.Easy), MatchRules.Minecraft, 60).WinRateA;
        // 1.8.9's head start (0.4.8) makes the first hit worth more, which narrows close matchups:
        // Hard beats Normal about 73% of 300 duels with it, 89% without.
        Check(hardOverNormal > 0.65, $"Hard beats Normal ({hardOverNormal:P0})");
        Check(normalOverEasy > 0.85, $"Normal beats Easy ({normalOverEasy:P0})");
    }

    public void TestAgainstTheOldBot()
    {
        // The 0.4.6 bot aimed perfectly and instantly; today's aims like a person but plays better.
        double hard = DuelSeries.Run(Skilled(BotDifficulty.Hard), seed => new LegacyBotBrain(seed), MatchRules.Minecraft, 80).WinRateA;
        double normal = DuelSeries.Run(Skilled(BotDifficulty.Normal), seed => new LegacyBotBrain(seed), MatchRules.Minecraft, 80).WinRateA;
        Check(hard > 0.7, $"Hard beats the 0.4.6 bot ({hard:P0})");
        Check(normal > 0.3 && normal < 0.7, $"Normal is about as strong as the 0.4.6 bot ({normal:P0})");
        // With the same eyes and hands, today's decisions beat the old ones.
        double decisions = DuelSeries.Run(
            Skilled(BotDifficulty.Normal), seed => new LegacyBotBrain(seed, Tuning.NormalBot, "old decisions"), MatchRules.Minecraft, 80).WinRateA;
        Check(decisions > 0.65, $"Normal's decisions beat the 0.4.6 bot's at the same aim ({decisions:P0})");
    }

    public void TestEasyCanBeComboed()
    {
        static double AverageCombo(BotDifficulty difficulty)
        {
            DuelSeries series = DuelSeries.Run(
                seed => new ComboAttacker(Tuning.NormalBot, seed), Skilled(difficulty), MatchRules.Minecraft, 40);
            double hits = 0;
            foreach (int combo in series.CombosA)
            {
                hits += combo;
            }
            return hits / Math.Max(series.CombosA.Count, 1);
        }
        double easy = AverageCombo(BotDifficulty.Easy);
        double hard = AverageCombo(BotDifficulty.Hard);
        Check(easy >= 3.5, $"a w-tapping player's combos on Easy average about 4 hits ({easy:0.0})");
        Check(hard <= 2, $"on Hard they're short ({hard:0.0})");
    }

    public void TestTimesItsClicksUnderWastedClicks()
    {
        var rules = MatchRules.Minecraft with { WastedClickDelay = 0.15 };
        DuelSeries series = DuelSeries.Run(Skilled(BotDifficulty.Normal), seed => new LegacyBotBrain(seed), rules, 40);
        double timed = series.WastedA / Math.Max(series.ClicksA, 1);
        double spam = series.WastedB / Math.Max(series.ClicksB, 1);
        Check(timed < spam * 0.6, $"wastes far fewer clicks than a spam clicker ({timed:P0} against {spam:P0})");
        Check(series.WinRateA > 0.7, $"and timing beats spam: Normal beats the 0.4.6 bot under wasted clicks ({series.WinRateA:P0})");
    }
}
