using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

/// <summary>
/// The player pushing twist (a match rule; 1.8.9 has none, players walk through each other).
/// Each game keeps its own player out of the others as it sees them, once per Minecraft tick.
/// Yaw -90 faces +X, yaw 90 faces -X.
/// </summary>
public class PushTests : TestCase
{
    private const int N = 3;
    private const double FloorY = 4;
    private const double Solid = 1.0;
    /// <summary>Our ticks of lag in a view: 6 is 100 ms, 9 is 150 ms.</summary>
    private const int Lag100 = 6;
    private const int Lag150 = 9;

    private static readonly MovementInput Idle = new(0, 0, false, true, false, 90);

    private static VoxelWorld FlatWorld(int wallX = -1)
    {
        var world = new VoxelWorld(new Vector3I(64, 24, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, (int)FloorY - 1, 63), Block.Stone);
        if (wallX >= 0)
        {
            world.Fill(new Vector3I(wallX, (int)FloorY, 0), new Vector3I(wallX, (int)FloorY + 3, 63), Block.Stone);
        }
        return world;
    }

    private static Vec3d At(double x, double z = 32.5) => new(x, FloorY, z);

    private static MovementInput Toward(double yaw, bool sprint = true, bool jump = false) => new(1, 0, jump, sprint, false, yaw);
    private static readonly MovementInput RunEast = Toward(-90);
    private static readonly MovementInput WalkEast = Toward(-90, sprint: false);
    private static readonly MovementInput HopEast = Toward(-90, jump: true);
    private static readonly MovementInput RunWest = Toward(90);
    private static readonly MovementInput WalkWest = Toward(90, sprint: false);

    /// <summary>
    /// Players each pushed by "their own game", the way Game.FindPushContacts and
    /// Player.PushContactWith do it: away from the others as seen <c>delay</c> ticks late (where a
    /// laggy game draws them), remembering which side of each other they came from.
    /// </summary>
    private sealed class Crowd
    {
        private readonly double _strength;
        private readonly int _delay;
        private readonly List<Vec3d>[] _feet;
        private readonly List<bool>[] _flying;
        private readonly Dictionary<int, Vec3d>[] _sides;

        public Crowd(VoxelWorld world, double strength, int delay, params Vec3d[] starts)
        {
            _strength = strength;
            _delay = delay;
            Motors = starts.Select(p => new PlayerMotor(world, p)).ToArray();
            _feet = starts.Select(_ => new List<Vec3d>()).ToArray();
            _flying = starts.Select(_ => new List<bool>()).ToArray();
            _sides = starts.Select(_ => new Dictionary<int, Vec3d>()).ToArray();
        }

        public PlayerMotor[] Motors { get; }

        /// <summary>How far apart two players' centres are along X (negative once the first is past the second).</summary>
        public double GapX(int a = 0, int b = 1) => Motors[b].Position.X - Motors[a].Position.X;

        /// <summary>How deep two players' bodies overlap (0 if they don't).</summary>
        public double Overlap(int a = 0, int b = 1) =>
            Math.Max(0, Tuning.PlayerWidth - (Motors[b].Position - Motors[a].Position).HorizontalLength);

        public void Step(params MovementInput[] keys)
        {
            for (int i = 0; i < Motors.Length; i++)
            {
                _feet[i].Add(Motors[i].Position);
                _flying[i].Add(Motors[i].KnockbackFlight);
            }
            for (int i = 0; i < Motors.Length; i++)
            {
                PlayerMotor motor = Motors[i];
                motor.PushStrength = _strength;
                motor.Contacts.Clear();
                var touching = new Dictionary<int, Vec3d>();
                for (int j = 0; j < Motors.Length && !motor.KnockbackFlight; j++)
                {
                    int seen = Math.Max(0, _feet[j].Count - 1 - _delay);
                    if (j == i || _flying[j][seen])
                    {
                        continue;
                    }
                    Vec3d feet = _feet[j][seen];
                    Vec3d move = feet - _feet[j][Math.Max(0, seen - N)];
                    Vec3d remembered = _sides[i].GetValueOrDefault(j);
                    Vec3d tieBreak = Pushing.TieBreak(i, j);
                    if (Pushing.Touching(motor.Position, feet))
                    {
                        touching[j] = Pushing.Side(motor.Position, feet, remembered, tieBreak);
                    }
                    motor.Contacts.Add(new PushContact(feet, move, remembered, tieBreak));
                }
                _sides[i] = touching;
            }
            for (int i = 0; i < Motors.Length; i++)
            {
                Motors[i].Step(keys[i]);
            }
        }
    }

    /// <summary>What happened over a run of two players.</summary>
    private sealed record Run(double MinGap, double MaxOverlap, double FinalGap, double Jitter, double SecondMoved, double SecondSpeed);

    /// <summary>
    /// Runs two players for <paramref name="seconds"/>. Jitter is the largest change, from one
    /// Minecraft tick to the next, of how far either player moved in a tick, over the last second:
    /// smooth motion keeps it near zero.
    /// </summary>
    private static Run Play(double strength, Vec3d a0, Vec3d b0, Func<int, MovementInput> keysA, Func<int, MovementInput> keysB,
        int delay = 0, double seconds = 5, int wallX = -1)
    {
        var crowd = new Crowd(FlatWorld(wallX), strength, delay, a0, b0);
        int steps = (int)(seconds * Tuning.SimRate);
        double minGap = double.MaxValue;
        double maxOverlap = 0;
        var tickA = new List<Vec3d>();
        var tickB = new List<Vec3d>();
        for (int s = 0; s < steps; s++)
        {
            crowd.Step(keysA(s), keysB(s));
            minGap = Math.Min(minGap, crowd.GapX());
            maxOverlap = Math.Max(maxOverlap, crowd.Overlap());
            if (s % N == N - 1)
            {
                tickA.Add(crowd.Motors[0].Position);
                tickB.Add(crowd.Motors[1].Position);
            }
        }
        int last = tickB.Count - 1;
        double speed = (tickB[last] - tickB[last - Tuning.McTickRate]).HorizontalLength;
        return new Run(minGap, maxOverlap, crowd.GapX(), Math.Max(Jitter(tickA), Jitter(tickB)),
            (crowd.Motors[1].Position - b0).HorizontalLength, speed);
    }

    private static double Jitter(List<Vec3d> ticks)
    {
        double worst = 0;
        for (int i = Math.Max(2, ticks.Count - Tuning.McTickRate); i < ticks.Count; i++)
        {
            Vec3d move = ticks[i] - ticks[i - 1];
            Vec3d before = ticks[i - 1] - ticks[i - 2];
            worst = Math.Max(worst, (move - before).HorizontalLength);
        }
        return worst;
    }

    private static Func<int, MovementInput> Always(MovementInput keys) => _ => keys;

    /// <summary>The situations two players get into, all on flat ground.</summary>
    private static IEnumerable<(string Name, Vec3d A, Vec3d B, Func<int, MovementInput> KeysA, Func<int, MovementInput> KeysB, int WallX)> Situations()
    {
        yield return ("walking into a standing player", At(30.5), At(33.5), Always(WalkEast), Always(Idle), -1);
        yield return ("sprinting into a standing player", At(30.5), At(33.5), Always(RunEast), Always(Idle), -1);
        yield return ("sprint-jumping into a standing player", At(30.5), At(33.5), Always(HopEast), Always(Idle), -1);
        yield return ("sprinting head-on", At(29.5), At(35.5), Always(RunEast), Always(RunWest), -1);
        yield return ("walking head-on", At(29.5), At(35.5), Always(WalkEast), Always(WalkWest), -1);
        yield return ("sprinting into a player against a wall", At(30.5), At(35.0), Always(RunEast), Always(Idle), 36);
    }

    public void TestOffIsMinecraft()
    {
        var contact = new PushContact(At(32.6), Vec3d.Zero, Vec3d.Zero, Pushing.TieBreak(1, 2));
        CheckEqual(Pushing.Resolve(At(32.5), new Vec3d(0.2, 0, 0), new Vec3d(0.1, 0, 0), [contact], 0), Vec3d.Zero,
            "no push when the twist is off");
        CheckEqual(Tuning.DefaultPushing, 0.0, "off by default");
        CheckEqual(MatchRules.Default.Pushing, 0.0, "plain 1.8.9 rules have it off");
        Run run = Play(0, At(30.5), At(33.5), Always(RunEast), Always(Idle));
        Check(run.FinalGap < -1, $"with the twist off you run straight through a standing player, like 1.8.9 ({run.FinalGap:0.0})");
        CheckNear(run.SecondMoved, 0, 1e-12, "and they don't move");

        // Contacts are ignored entirely at 0: the path is exactly 1.8.9's.
        VoxelWorld world = FlatWorld();
        var plain = new PlayerMotor(world, At(30.5));
        var withContacts = new PlayerMotor(world, At(30.5));
        withContacts.Contacts.Add(new PushContact(At(31.0), Vec3d.Zero, Vec3d.Zero, new Vec3d(1, 0, 0)));
        for (int s = 0; s < 120; s++)
        {
            plain.Step(RunEast);
            withContacts.Step(RunEast);
        }
        CheckEqual(withContacts.Position, plain.Position, "a motor at 0% ignores the players around it");
    }

    /// <summary>
    /// Our 60 ticks/s motor pushes exactly as a 20 ticks/s Minecraft player doing the same thing
    /// once per tick would (<see cref="McReference"/>): positions match at every Minecraft tick,
    /// on the ground and sprint-jumping, walking into one player while another moves alongside.
    /// </summary>
    public void TestPushingKeepsMinecraftsRhythm()
    {
        foreach (bool jumping in new[] { false, true })
        {
            VoxelWorld world = FlatWorld();
            var mc = new McReference(world, At(30.5)) { PushStrength = Solid };
            var ours = new PlayerMotor(world, At(30.5)) { PushStrength = Solid };
            var free = new PlayerMotor(world, At(30.5));
            for (int t = 0; t < 5; t++)
            {
                // Settle onto the ground first, like the other comparisons with 1.8.9.
                mc.Tick(Idle);
                for (int s = 0; s < N; s++)
                {
                    ours.Step(Idle);
                    free.Step(Idle);
                }
            }
            double worst = 0;
            double pushed = 0;
            for (int t = 0; t < 60; t++)
            {
                var keys = new MovementInput(1, t % 20 < 10 ? 0.5 : -0.5, jumping, true, false, -90);
                var others = new List<PushContact>
                {
                    new(At(33.0), Vec3d.Zero, Vec3d.Zero, new Vec3d(-1, 0, 0)),
                    new(At(31.0 + t * 0.05, 32.0), new Vec3d(0.05, 0, 0), Vec3d.Zero, new Vec3d(-1, 0, 0)),
                };
                mc.Contacts.Clear();
                mc.Contacts.AddRange(others);
                ours.Contacts.Clear();
                ours.Contacts.AddRange(others);
                mc.Tick(keys);
                for (int s = 0; s < N; s++)
                {
                    ours.Step(keys);
                    free.Step(keys);
                }
                worst = Math.Max(worst, (ours.Position - mc.Position).Length);
                pushed = Math.Max(pushed, (ours.Position - free.Position).Length);
            }
            string name = jumping ? "sprint-jumping" : "on the ground";
            Check(pushed > 0.5, $"{name}: the other players really pushed ({pushed:0.00} blocks)");
            Check(worst < 1e-9, $"{name}: off by {worst:E2} at worst");
        }
    }

    public void TestNobodyRunsThroughAnyoneAtFullStrength()
    {
        foreach (var (name, a, b, keysA, keysB, wallX) in Situations())
        {
            Run run = Play(Solid, a, b, keysA, keysB, wallX: wallX);
            Check(run.MinGap > Tuning.PlayerWidth - Tuning.PushSquish - 0.01,
                $"{name}: never deeper than the squish ({Tuning.PlayerWidth - run.MinGap:0.00} blocks of overlap)");
            // Bunny-hopping into someone lurches with every hop, which isn't wobble.
            double steady = name.Contains("jumping", StringComparison.Ordinal) ? 0.02 : 0.002;
            Check(run.Jitter < steady, $"{name}: steady ({run.Jitter:0.0000} blocks per tick of wobble)");
        }
    }

    public void TestRunningIntoAStandingPlayerShovesThem()
    {
        Run run = Play(Solid, At(30.5), At(33.5), Always(RunEast), Always(Idle), seconds: 5);
        double speed = run.SecondSpeed;
        Check(speed > 1.0 && speed < 2.0, $"shoved along at a slow walk ({speed:0.0} blocks/s; walking is 4.3)");
        Check(run.SecondMoved > 3, $"keeps shoving ({run.SecondMoved:0.0} blocks in 5 s)");

        // Let go after two seconds: both stop, apart, and nobody slides off.
        Run stop = Play(Solid, At(30.5), At(33.5), s => s < 120 ? RunEast : Idle, Always(Idle), seconds: 4);
        Check(stop.FinalGap >= Tuning.PlayerWidth - 0.001, $"they end apart ({stop.FinalGap:0.00})");
        Check(stop.FinalGap < Tuning.PlayerWidth + 0.3, $"without being flung apart ({stop.FinalGap:0.00})");
        CheckNear(stop.SecondSpeed, 0, 1e-9, "and everything comes to rest");
    }

    public void TestPlayersCaughtInsideEachOtherSlideApart()
    {
        foreach (var (name, gap) in new[] { ("spawned 0.1 apart", 0.1), ("spawned on the same spot", 0.0) })
        {
            var crowd = new Crowd(FlatWorld(), Solid, 0, At(32.5 - gap / 2), At(32.5 + gap / 2));
            double fastest = 0;
            double apartAt = double.NaN;
            for (int s = 0; s < 120; s++)
            {
                crowd.Step(Idle, Idle);
                fastest = Math.Max(fastest, crowd.Motors.Max(m => m.HorizontalSpeed));
                if (double.IsNaN(apartAt) && crowd.Overlap() < 0.01)
                {
                    apartAt = (s + 1) / (double)Tuning.SimRate;
                }
            }
            Check(apartAt <= 0.35, $"{name}: apart within a third of a second ({apartAt:0.00} s)");
            Check(fastest <= 3.1, $"{name}: a slide, not a launch ({fastest:0.0} blocks/s each)");
            Check(crowd.Motors.All(m => m.HorizontalSpeed < 1e-9), $"{name}: then at rest");
            Check(crowd.Overlap() == 0, $"{name}: and apart");
        }
        Check(Pushing.TieBreak(1, 2) == Pushing.TieBreak(2, 1) * -1, "both games agree who goes which way");
    }

    public void TestWalkingPastSomeoneSlidesAround()
    {
        Run run = Play(Solid, At(29.5, 32.25), At(32.5, 32.5), Always(WalkEast), Always(Idle), seconds: 3);
        Check(run.FinalGap < -2, $"you get past ({run.FinalGap:0.0})");
        Check(run.MaxOverlap < Tuning.PushSquish, $"brushing them, not through them ({run.MaxOverlap:0.00} overlap)");
        Check(run.SecondMoved < 0.5, $"they're nudged aside a little ({run.SecondMoved:0.00} blocks)");
        Check(run.Jitter < 0.002, $"smoothly ({run.Jitter:0.0000})");
    }

    /// <summary>
    /// Each game sees the others late (where it draws them). Pushing still settles, with no
    /// back-and-forth, and two players running into each other end on their own sides.
    /// </summary>
    public void TestSteadyUnderLag()
    {
        foreach (int delay in new[] { Lag100, Lag150 })
        {
            string lag = $"{delay * 1000 / Tuning.SimRate} ms";
            foreach (var (name, a, b, keysA, keysB, wallX) in Situations())
            {
                Run run = Play(Solid, a, b, keysA, keysB, delay, wallX: wallX);
                Check(run.FinalGap > Tuning.PlayerWidth - Tuning.PushSquish - 0.05,
                    $"{name}, {lag} late: ends on their own sides, barely squished ({run.FinalGap:0.00})");
                double steady = name.Contains("jumping", StringComparison.Ordinal) ? 0.02 : 0.002;
                Check(run.Jitter < steady, $"{name}, {lag} late: steady ({run.Jitter:0.0000} blocks per tick of wobble)");
            }
            Run shove = Play(Solid, At(30.5), At(33.5), Always(RunEast), Always(Idle), delay);
            Check(shove.SecondMoved > 1, $"{lag} late: still shoves ({shove.SecondMoved:0.0} blocks in 5 s)");
        }
    }

    /// <summary>
    /// Below 100% bodies are softer. From 60% up nobody gets through, with more squish the lower
    /// it is; below that you can push through someone, slowed, and they slide apart.
    /// </summary>
    public void TestSofterSettings()
    {
        double previousSquish = double.MaxValue;
        for (int step = 1; step <= 10; step++)
        {
            double strength = step / 10.0;
            Run walk = Play(strength, At(30.5), At(33.5), Always(WalkEast), Always(Idle));
            Run headOn = Play(strength, At(29.5), At(35.5), Always(WalkEast), Always(WalkWest));
            Check(walk.Jitter < 0.002 && headOn.Jitter < 0.002, $"{step * 10}%: steady ({walk.Jitter:0.0000}, {headOn.Jitter:0.0000})");
            if (step >= 6)
            {
                Check(walk.MinGap > 0 && headOn.MinGap > 0, $"{step * 10}%: nobody gets through");
                double squish = Tuning.PlayerWidth - headOn.FinalGap;
                Check(squish <= previousSquish + 1e-9, $"{step * 10}%: firmer than below ({squish:0.00} overlap)");
                previousSquish = squish;
            }
            else
            {
                Check(walk.FinalGap < -1, $"{step * 10}%: you can push through a standing player ({walk.FinalGap:0.0})");
            }
        }
        // Squeezing through takes longer the firmer it is.
        double Through(double strength)
        {
            var crowd = new Crowd(FlatWorld(), strength, 0, At(30.5), At(33.5));
            for (int s = 0; s < 600; s++)
            {
                crowd.Step(WalkEast, Idle);
                if (crowd.GapX() < -Tuning.PlayerWidth)
                {
                    return s / (double)Tuning.SimRate;
                }
            }
            return double.PositiveInfinity;
        }
        double off = Through(0);
        double soft = Through(0.3);
        double softer = Through(0.5);
        Check(off < soft && soft < softer, $"slower to get through as it firms up ({off:0.00} s, {soft:0.00} s at 30%, {softer:0.00} s at 50%)");
    }

    /// <summary>
    /// Knockback always stays whole: a player flying from a hit passes through others until they
    /// land, and the others aren't pushed by them either. Then any overlap sorts itself out.
    /// </summary>
    public void TestKnockbackStaysWhole()
    {
        var knockback = new Vec3d(0.9, 0.4, 0);
        VoxelWorld world = FlatWorld();
        var mc = new McReference(world, At(30.5)) { PushStrength = Solid };
        var crowd = new Crowd(world, Solid, 0, At(30.5), At(31.4));
        var alone = new PlayerMotor(world, At(30.5));
        for (int s = 0; s < 15; s++)
        {
            crowd.Step(Idle, Idle);
            alone.Step(Idle);
            if (s % N == N - 1)
            {
                mc.Tick(Idle);
            }
        }
        Vec3d bystander = crowd.Motors[1].Position;
        crowd.Motors[0].SetMcMotion(knockback);
        alone.SetMcMotion(knockback);
        mc.ReceiveVelocity(knockback);
        Check(crowd.Motors[0].KnockbackFlight, "flying after the hit");
        Check((crowd.Motors[0].Flags & PlayerFlags.KnockbackFlight) != 0, "and everyone else is told");
        double worst = 0;
        double bystanderMoved = 0;
        double overlapped = 0;
        int landedAt = -1;
        for (int s = 0; s < 120; s++)
        {
            bool flying = crowd.Motors[0].KnockbackFlight;
            crowd.Step(Idle, Idle);
            alone.Step(Idle);
            if (s % N == N - 1)
            {
                mc.Tick(Idle);
                worst = Math.Max(worst, (crowd.Motors[0].Position - mc.Position).Length);
            }
            if (flying)
            {
                worst = Math.Max(worst, (crowd.Motors[0].Position - alone.Position).Length);
                bystanderMoved = Math.Max(bystanderMoved, (crowd.Motors[1].Position - bystander).HorizontalLength);
                overlapped = Math.Max(overlapped, crowd.Overlap());
            }
            else if (landedAt < 0)
            {
                landedAt = s;
            }
        }
        Check(overlapped > 0.3, $"the flight goes right through the player behind ({overlapped:0.00} overlap)");
        Check(worst < 1e-9, $"exactly as if nobody were there, and as 1.8.9 flies it (off by {worst:E2})");
        CheckNear(bystanderMoved, 0, 1e-12, "the player behind isn't pushed by the flying one");
        Check(landedAt > N, $"the flight ends on landing, not right after the hit (tick {landedAt})");
        Check(!crowd.Motors[0].KnockbackFlight, "landed");
        Check(crowd.Overlap() == 0, "after landing, nobody is left inside anyone");
    }

    /// <summary>Three in a row: walking into one player shoves them into the next, smoothly.</summary>
    public void TestThreeInARow()
    {
        var crowd = new Crowd(FlatWorld(), Solid, 0, At(29.5), At(32.5), At(33.2));
        var ticks = new List<Vec3d>[] { new(), new(), new() };
        for (int s = 0; s < 300; s++)
        {
            crowd.Step(RunEast, Idle, Idle);
            if (s % N == N - 1)
            {
                for (int i = 0; i < 3; i++)
                {
                    ticks[i].Add(crowd.Motors[i].Position);
                }
            }
        }
        Check(crowd.GapX(0, 1) > 0.3 && crowd.GapX(1, 2) > 0.3, $"in order ({crowd.GapX(0, 1):0.00}, {crowd.GapX(1, 2):0.00})");
        Check(crowd.Motors[2].Position.X > 34, $"the last one is shoved along too ({crowd.Motors[2].Position.X - 33.2:0.0} blocks)");
        double jitter = ticks.Max(Jitter);
        Check(jitter < 0.002, $"steady ({jitter:0.0000})");
    }

    /// <summary>
    /// Four players walking into each other from four sides (a scrum), at any lag: nobody ends up
    /// inside anyone, and it stays steady.
    /// </summary>
    public void TestScrum()
    {
        foreach (int delay in new[] { 0, Lag100, Lag150 })
        {
            Vec3d centre = At(32.5, 32.5);
            Vec3d[] starts = [At(30.5, 32.5), At(34.5, 32.4), At(32.6, 30.5), At(32.4, 34.5)];
            var crowd = new Crowd(FlatWorld(), Solid, delay, starts);
            var ticks = starts.Select(_ => new List<Vec3d>()).ToArray();
            // Everyone holds W, facing the middle from where they started.
            MovementInput[] keys = starts.Select(p =>
            {
                Vec3d to = centre - p;
                return Toward(Math.Atan2(-to.X, -to.Z) * 180 / Math.PI);
            }).ToArray();
            double deepest = 0;
            for (int s = 0; s < 360; s++)
            {
                crowd.Step(keys);
                if (s >= 120)
                {
                    for (int a = 0; a < 4; a++)
                    {
                        for (int b = a + 1; b < 4; b++)
                        {
                            deepest = Math.Max(deepest, crowd.Overlap(a, b));
                        }
                    }
                }
                if (s % N == N - 1)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        ticks[i].Add(crowd.Motors[i].Position);
                    }
                }
            }
            string lag = $"{delay * 1000 / Tuning.SimRate} ms";
            double jitter = ticks.Max(Jitter);
            Check(deepest < Tuning.PushSquish + (delay == 0 ? 0.05 : 0.15), $"{lag}: nobody inside anyone ({deepest:0.00} overlap at worst)");
            Check(jitter < 0.002, $"{lag}: steady ({jitter:0.0000} blocks per tick of wobble)");
        }
    }

    public void TestSidesAreRemembered()
    {
        Vec3d west = new(-1, 0, 0);
        Vec3d tieBreak = new(1, 0, 0);
        CheckEqual(Pushing.Side(At(32.4), At(32.6), Vec3d.Zero, tieBreak), west, "the side we're on");
        CheckEqual(Pushing.Side(At(32.7), At(32.6), west, tieBreak), west, "centres passed: the side we came from stays");
        CheckEqual(Pushing.Side(At(32.6), At(32.6), Vec3d.Zero, tieBreak), tieBreak, "on top of each other: the tie-break");
        Vec3d slid = Pushing.Side(At(32.4, 32.8), At(32.6), west, tieBreak);
        Check(slid.Z > 0 && slid.X < 0, "sliding round each other follows along");
        Check(Pushing.Touching(At(32.5), At(33.3)) && !Pushing.Touching(At(32.5), At(34.0)), "only remembered while close");
    }
}
