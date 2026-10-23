using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Movement;

namespace UhcPvp.Tests;

/// <summary>
/// The combo simulation: how long combos get against each way a victim can respond, with the
/// game's own movement (<see cref="PlayerMotor"/>), knockback (<see cref="CombatRules"/>, built
/// from the server's copy, with combo knockback) and drawing of others (<see cref="HeadStart"/>
/// over a <see cref="SnapshotBuffer"/>), on flat ground, with perfect aim.
/// - The attacker sprints at a standing victim and lands the first hit at full reach (a sprint
///   hit). From then on the victim does one fixed thing (keys relative to facing the attacker).
/// - The attacker always faces the victim as drawn and holds W (sprint is always on), with one of
///   these, the best of each tried: nothing more; a w-tap or s-tap of 1 to 4 ticks at any moment
///   in the first half second after each hit; any of those while sprint-jumping; or keeping its
///   distance (letting go of W, or holding S, while the victim is closer than 3 to 4 blocks
///   during their protection).
/// - A hit lands the first moment hit protection is over (10 Minecraft ticks on the 1/20 s grid,
///   from the click) and the victim is drawn within reach.
/// - The combo breaks when the victim, 100 ms or more after a hit, has the attacker drawn within
///   reach before the attacker's next hit (ties go to the attacker), or when the attacker isn't in
///   reach within 250 ms of protection ending. 8 hits is the cap.
/// Averaged over 3 timings of the 1/20 s grid. Printing runs only when asked:
///   COMBO_SIM=Table tools/run_tests.sh ComboSim   the table in docs/combat.md (about 2 minutes)
///   COMBO_SIM=Port tools/run_tests.sh ComboSim    1.8.9's own drawing (McView) against our fixed delays
/// </summary>
public class ComboSim : TestCase
{
    private const int N = Tuning.SubticksPerMcTick;
    private const double FloorY = 4;
    private const int MaxHits = 8;
    /// <summary>The victim can't hit back in the first 100 ms after being hit (reaction).</summary>
    private const int ReactionSubticks = 2 * N;
    /// <summary>MC ticks after protection ends for the attacker to land the next hit.</summary>
    private const int WindowTicks = 5;
    private static readonly double Step = 1.0 / Tuning.SimRate;

    private sealed class FlatFloor : ICollisionWorld
    {
        private static readonly Box Slab = new(-1e7, FloorY - 1, -1e7, 1e7, FloorY, 1e7);

        public void CollectBoxes(Box region, List<Box> output)
        {
            if (region.MinY < FloorY && region.MaxY > FloorY - 1)
            {
                output.Add(Slab);
            }
        }
    }

    /// <summary>The rules of a run: the head start (seconds), combo knockback, or 1.8.9's own drawing instead of a head start.</summary>
    public sealed record Rules(string Name, double HeadStart, double ComboKnockback = 1.0, bool Minecraft = false);

    public enum Tap { None, W, S, SpaceW, SpaceS }

    /// <summary>
    /// Release W (or press S) for TapTicks MC ticks, starting TapOffset subticks after each hit;
    /// SpaceW/SpaceS: while the victim's protection lasts, release W (or press S) whenever they're
    /// drawn closer than Space (reach).
    /// </summary>
    public readonly record struct Strategy(Tap Tap, int TapTicks, int TapOffset, bool Jump, double Space = 0);

    public readonly record struct VictimView(int SinceFirstHit, int SinceLastHit);
    public readonly record struct VictimKeys(double Forward, double Strafe, bool Jump);
    public sealed record VictimPlan(string Name, Func<VictimView, VictimKeys> Keys);

    private static double YawTo(Vec3d from, Vec3d to) => Math.Atan2(-(to.X - from.X), -(to.Z - from.Z)) * 180 / Math.PI;

    /// <summary>Reach as HitScan measures it with perfect aim: eyes to the nearest point of the enlarged hitbox.</summary>
    private static double Reach(Vec3d feet, Vec3d other) =>
        HitScan.Hitbox(other).DistanceTo(feet + new Vec3d(0, Tuning.EyeHeight, 0));

    /// <summary>1.8.9's tick phases (server, watching client) per grid timing: pipelines of 0.5, 1 and 1.5 ticks, 175 ms on average.</summary>
    private static readonly (double Server, double Viewer)[] MinecraftPhases = [(0.25, 0.5), (0.5, 0.0), (0.75, 0.5)];

    /// <summary>Where one player is drawn on the other's screen.</summary>
    private sealed class View
    {
        private readonly SnapshotBuffer _states = new();
        private readonly double _delay;
        private readonly McView? _minecraft;
        private double _time;
        private double _yaw;
        private double? _lastYaw;

        public View(Rules rules, int grid, Vec3d spawn)
        {
            _delay = HeadStart.ViewDelay(rules.HeadStart, owned: true, 0);
            if (rules.Minecraft)
            {
                var (server, viewer) = MinecraftPhases[grid % MinecraftPhases.Length];
                _minecraft = new McView(spawn, 0, server * 0.05, viewer * 0.05);
            }
        }

        /// <summary>A step ended: the watched player is here now.</summary>
        public void Record(PlayerMotor m, double yaw)
        {
            _time += Step;
            _states.Push(_time, new PlayerState(m.Position.ToGodot(), 0, 0, PlayerFlags.None, 20));
            if (_minecraft is not null)
            {
                // Unwrapped yaw, so turning past 180 isn't a big jump.
                _yaw += _lastYaw is double last ? Math.IEEERemainder(yaw - last, 360) : 0;
                _lastYaw = yaw;
                if (Math.Round(_time / Step) % N == 0)
                {
                    _minecraft.Report(_time, m.Position, m.IsTouchingGround, _yaw, 0, m.Sprinting ? 1 : 0);
                }
                _minecraft.Advance(_time + 1e-9);
            }
        }

        /// <summary>The watched player was hit now (forces a tracker update in 1.8.9).</summary>
        public void Hit() => _minecraft?.Hit(_time);

        public Vec3d Drawn => _minecraft?.Hitbox ?? Vec3d.From(_states.Sample(_time - _delay).Position);
    }

    /// <summary>One combo: how many hits it got and how it ended.</summary>
    private static (int Hits, string Break) Run(Rules rules, Strategy st, VictimPlan plan, int grid)
    {
        var world = new FlatFloor();
        var a = new PlayerMotor(world, new Vec3d(0.5, FloorY, 12.5));
        var v = new PlayerMotor(world, new Vec3d(0.5, FloorY, 0.5));
        // aSeesV: the victim as drawn on the attacker's screen; vSeesA: the attacker on the victim's.
        var aSeesV = new View(rules, grid, v.Position);
        var vSeesA = new View(rules, grid, a.Position);
        double aYaw = 0;
        double vYaw = 180;
        void Advance(double forward, Strategy? strategy, VictimKeys keys)
        {
            aYaw = YawTo(a.Position, aSeesV.Drawn);
            a.Step(new MovementInput(forward, 0, strategy?.Jump ?? false, true, false, aYaw));
            vYaw = YawTo(v.Position, vSeesA.Drawn);
            v.Step(new MovementInput(keys.Forward, keys.Strafe, keys.Jump, true, false, vYaw));
            aSeesV.Record(v, vYaw);
            vSeesA.Record(a, aYaw);
        }
        for (int i = 0; i < 5 * N; i++)
        {
            Advance(0, null, default);
        }
        // The approach: the attacker sprints at the standing victim; hit 1 at full reach.
        int s = 0;
        while (Reach(a.Position, aSeesV.Drawn) > Tuning.Reach)
        {
            Advance(1, null, default);
            s++;
        }
        long TickOf(int subtick) => (subtick + grid) / N;
        int hits = 0;
        int firstHitS = s;
        int lastHitS = s;
        long lastHitTick = 0;
        int protectionEndS = -1;
        var random = new Random(1);

        void Hit()
        {
            // Pushed away from where the attacker really is, along where it faces (the victim as drawn).
            double yaw = YawTo(a.Position, aSeesV.Drawn);
            bool sprint = a.ServerSprinting;
            Vec3d attackerAt = a.Position;
            Vec3d victimAt = v.Position;
            v.Knock(copy => CombatRules.VelocityPacket(
                CombatRules.Knockback(copy, attackerAt, victimAt, yaw, true, sprint, random, rules.ComboKnockback)));
            aSeesV.Hit();
            a.OnHitLanded(accepted: true);
            hits++;
            lastHitS = s;
            lastHitTick = TickOf(s);
            protectionEndS = -1;
        }

        Hit();
        while (true)
        {
            int since = s - lastHitS;
            bool protectedNow = TickOf(s) - lastHitTick < Tuning.HurtImmunityThresholdMcTicks;
            double forward = 1;
            if (st.Tap is Tap.W or Tap.S && since >= st.TapOffset && since < st.TapOffset + st.TapTicks * N)
            {
                forward = st.Tap == Tap.W ? 0 : -1;
            }
            if (st.Tap is Tap.SpaceW or Tap.SpaceS && protectedNow && Reach(a.Position, aSeesV.Drawn) < st.Space)
            {
                forward = st.Tap == Tap.SpaceW ? 0 : -1;
            }
            Advance(forward, st, plan.Keys(new VictimView(s - firstHitS, s - lastHitS)));
            s++;
            since = s - lastHitS;
            bool victimIn = Reach(v.Position, vSeesA.Drawn) <= Tuning.Reach;
            bool attackerIn = Reach(a.Position, aSeesV.Drawn) <= Tuning.Reach;
            if (TickOf(s) - lastHitTick < Tuning.HurtImmunityThresholdMcTicks)
            {
                if (since >= ReactionSubticks && victimIn)
                {
                    return (hits, "hit back");
                }
                continue;
            }
            if (protectionEndS < 0)
            {
                protectionEndS = s;
            }
            if (attackerIn)
            {
                Hit();
                if (hits >= MaxHits)
                {
                    return (hits, "max");
                }
                continue;
            }
            if (victimIn)
            {
                return (hits, "hit back");
            }
            if (s - protectionEndS >= WindowTicks * N)
            {
                return (hits, "out of reach");
            }
        }
    }

    // --- Victim responses ------------------------------------------------------------------

    private static VictimPlan Switching(int ticks) =>
        new($"switches A/D every {ticks} ticks", v => new VictimKeys(0, v.SinceFirstHit / (ticks * N) % 2 == 0 ? 1 : -1, false));

    private static readonly VictimPlan NoKeys = new("no keys", _ => new VictimKeys(0, 0, false));
    private static readonly VictimPlan Strafing = new("strafes (A)", _ => new VictimKeys(0, 1, false));

    private static List<VictimPlan> Victims() =>
    [
        NoKeys,
        new("holds W (runs back in)", _ => new VictimKeys(1, 0, false)),
        new("holds S (backs off)", _ => new VictimKeys(-1, 0, false)),
        Strafing,
        new("S+A", _ => new VictimKeys(-1, 1, false)),
        new("W+A", _ => new VictimKeys(1, 1, false)),
        Switching(2), Switching(3), Switching(4), Switching(6), Switching(8),
        new("jumps on the hit", v => new VictimKeys(0, 0, v.SinceLastHit < N)),
        new("jumps on the hit, holding W", v => new VictimKeys(1, 0, v.SinceLastHit < N)),
        new("holds jump", _ => new VictimKeys(0, 0, true)),
        new("holds jump and W", _ => new VictimKeys(1, 0, true)),
    ];

    // --- Attacker strategies -----------------------------------------------------------------

    private static IEnumerable<Strategy> Candidates(bool spacing)
    {
        foreach (bool jump in new[] { false, true })
        {
            yield return new Strategy(Tap.None, 0, 0, jump);
            for (int offset = 0; offset < 30; offset++)
            {
                for (int ticks = 1; ticks <= 4; ticks++)
                {
                    if (ticks <= 3)
                    {
                        yield return new Strategy(Tap.W, ticks, offset, jump);
                    }
                    yield return new Strategy(Tap.S, ticks, offset, jump);
                }
            }
            if (spacing)
            {
                for (int d = 30; d <= 40; d++)
                {
                    yield return new Strategy(Tap.SpaceW, 0, 0, jump, d / 10.0);
                    yield return new Strategy(Tap.SpaceS, 0, 0, jump, d / 10.0);
                }
            }
        }
    }

    /// <summary>The attacker's best strategy against a victim: most hits on average over the 3 grid timings.</summary>
    private static double Best(Rules rules, VictimPlan plan, bool spacing = true)
    {
        Strategy[] list = Candidates(spacing).ToArray();
        var scores = new double[list.Length];
        Parallel.For(0, list.Length, i =>
        {
            double sum = 0;
            for (int g = 0; g < N; g++)
            {
                sum += Run(rules, list[i], plan, g).Hits;
            }
            scores[i] = sum / N;
        });
        return scores.Max();
    }

    // --- A real test ----------------------------------------------------------------------------

    public void TestHeadStartLengthensCombos()
    {
        // The reason for the head start: drawn fresh, a victim who lets the knockback carry them,
        // or strafes, is back in reach as soon as the attacker and the combo ends at 1 hit; with
        // 1.8.9's 175 ms the attacker gets there first.
        foreach (VictimPlan plan in new[] { NoKeys, Strafing })
        {
            double fresh = Best(new Rules("off", 0), plan, spacing: false);
            double minecraft = Best(new Rules("1.8.9", Tuning.DefaultHeadStart), plan, spacing: false);
            Check(fresh <= 1.5, $"{plan.Name}, no head start: {fresh:0.0} hits");
            Check(minecraft >= 3.5, $"{plan.Name}, 1.8.9's head start: {minecraft:0.0} hits");
        }
    }

    // --- Printed on request --------------------------------------------------------------------

    public void TestPrint()
    {
        string which = OS.GetEnvironment("COMBO_SIM");
        if (which == "Table")
        {
            var models = new List<Rules>();
            foreach (double headStart in new[] { 0.0, Tuning.DefaultHeadStart, Tuning.MaxHeadStart })
            {
                foreach (double combo in new[] { 1.0, 0.8, 0.6 })
                {
                    models.Add(new Rules($"{headStart * 1000:0} ms, {combo:P0}", headStart, combo));
                }
            }
            PrintTable("Combo length by head start and combo knockback", models);
        }
        else if (which == "Port")
        {
            PrintTable("1.8.9's own drawing against fixed delays",
            [
                new Rules("1.8.9 (port)", 0, Minecraft: true),
                new Rules("175 ms", 0.175),
                new Rules("125 ms", 0.125),
                new Rules("250 ms", 0.25),
            ]);
        }
    }

    private static void PrintTable(string title, List<Rules> models)
    {
        GD.Print($"\n## {title} (best attacker strategy, average of 3 tick timings, 8 is the cap)");
        GD.Print($"| Victim | {string.Join(" | ", models.Select(m => m.Name))} |");
        GD.Print($"|---|{string.Concat(models.Select(_ => "---|"))}");
        foreach (VictimPlan plan in Victims())
        {
            GD.Print($"| {plan.Name} | {string.Join(" | ", models.Select(m => $"{Best(m, plan):0.0}"))} |");
        }
    }
}
