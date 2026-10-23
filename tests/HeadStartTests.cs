using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

/// <summary>
/// The head start (1.8.9's display delay, <see cref="HeadStart"/>) against a port of how 1.8.9
/// shows other players (<see cref="McView"/>: its entity tracker and client glide), plus the
/// pieces the game builds it from.
/// </summary>
public class HeadStartTests : TestCase
{
    private const int N = Tuning.SubticksPerMcTick;
    private const double FloorY = 4;
    private static readonly double Step = 1.0 / Tuning.SimRate;

    // --- What 1.8.9 does -----------------------------------------------------------------------

    /// <summary>
    /// 1.8.9's delay for a player running at a steady sprint, at 0 ping: how far behind their real
    /// position a click finds them and the screen shows them, in seconds, over every phase of
    /// their client's, the server's and the watching client's ticks, and every click moment.
    /// </summary>
    private static (double Hitbox, double HitboxMin, double HitboxMax, double Rendered) SteadyDelay(double direction)
    {
        const double Speed = 5.612;
        var hit = new List<double>();
        var shown = new List<double>();
        const int Phases = 12;
        for (int reportPhase = 0; reportPhase < N; reportPhase++)
        {
            for (int s = 0; s < Phases; s++)
            {
                for (int v = 0; v < Phases; v++)
                {
                    var view = new McView(Vec3d.Zero, 0, (s + 0.5) / Phases * 0.05, (v + 0.25) / Phases * 0.05);
                    Vec3d At(double t) => new(direction * Speed * Math.Max(t, 0), 0, 0);
                    for (int step = 0; step < 180; step++)
                    {
                        double t = step * Step;
                        if (step % N == reportPhase)
                        {
                            view.Report(t, At(t), true, 0, 0, 0);
                        }
                        for (int c = 0; c < 10; c++)
                        {
                            double click = t + c * Step / 10;
                            view.Advance(click);
                            if (step >= 120)
                            {
                                hit.Add((At(click).X - view.Hitbox.X) / (direction * Speed));
                                shown.Add((At(click).X - view.Rendered(click).X) / (direction * Speed));
                            }
                        }
                    }
                }
            }
        }
        return (hit.Average(), hit.Min(), hit.Max(), shown.Average());
    }

    public void TestThePortBehavesLike189()
    {
        // The client's glide: a third of the way, then half the rest, then the rest: a straight line over 3 ticks.
        var drawn = new McOtherPlayer(Vec3d.Zero);
        drawn.Receive(new McMove(false, 96, 0, 0));
        var xs = new List<double>();
        for (int i = 0; i < 4; i++)
        {
            drawn.Tick();
            xs.Add(drawn.Position.X);
        }
        Check(xs.SequenceEqual([1.0, 2.0, 3.0, 3.0]), $"glides 3 blocks in 3 equal steps ({string.Join(", ", xs)})");

        // The tracker: every 2nd tick, sooner when forced, only after moving 4/32 block, and a
        // teleport when the on-ground flag changes.
        var tracker = new McTrackerEntry(Vec3d.Zero, true, 0, 0);
        McMove? Update(double x, bool onGround = true, bool forced = false) =>
            tracker.Update(new Vec3d(x, 0, 0), onGround, 0, 0, forced);
        Check(Update(1) is null, "nothing on its first tick, which quietly takes the position as sent");
        Check(Update(2) is null, "nor on an odd tick");
        CheckEqual(Update(3), new McMove(false, 64, 0, 0), "the move since then, in 1/32 block");
        CheckEqual(Update(4, forced: true), new McMove(false, 32, 0, 0), "an odd tick, forced (a jump or a hit)");
        Check(Update(4) is null && Update(4.1) is null, "less than 4/32 block: nothing");
        CheckEqual(Update(4.2, onGround: false), new McMove(true, 134, 0, 0), "leaving the ground: a teleport");
    }

    public void TestMinecraftShowsOthers175MsLate()
    {
        var (forward, min, max, rendered) = SteadyDelay(1);
        var (backward, _, _, renderedBack) = SteadyDelay(-1);
        // Rounding positions down to 1/32 block puts them a hair behind one way and ahead the other.
        double hitbox = (forward + backward) / 2;
        CheckNear(hitbox, Tuning.McHeadStart, 0.003, "where a click finds them, on average: 1.8.9's head start");
        CheckNear(min, 0.1, 0.01, "at best (tick timing)");
        CheckNear(max, Tuning.MaxHeadStart, 0.01, "at worst: the slider's maximum");
        CheckNear((rendered + renderedBack) / 2, 0.2, 0.003, "the screen shows them a tick later still");
    }

    // --- Our fixed delay against 1.8.9's drawing ------------------------------------------------

    private static VoxelWorld FlatWorld()
    {
        var world = new VoxelWorld(new Vector3I(96, 24, 96));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(95, 3, 95), Block.Stone);
        return world;
    }

    /// <summary>A player's state after each step: where, on the ground, flags, and whether a hit landed on them.</summary>
    private readonly record struct Frame(Vec3d Feet, bool OnGround, int Flags, double Yaw, bool Hit);

    /// <summary>
    /// A player moving for 6 seconds: running in a straight line ("sprint"), switching between A
    /// and D every 0.4 s ("strafe"), or comboed: a sprint hit every 0.5 s from the direction they
    /// face, while they hold W ("combo, holding W") or let it carry them ("combo, no keys").
    /// </summary>
    private static List<Frame> Path(string kind)
    {
        var motor = new PlayerMotor(FlatWorld(), new Vec3d(48.5, FloorY, 48.5));
        var frames = new List<Frame>();
        var random = new Random(3);
        for (int step = 0; step < 6 * Tuning.SimRate; step++)
        {
            double t = step * Step;
            bool hit = kind.StartsWith("combo", StringComparison.Ordinal) && step % 30 == 0 && step > 0;
            // Comboed back and forth across the middle, so it stays on the floor.
            double yaw = kind.StartsWith("combo", StringComparison.Ordinal) ? (step / 120 % 2 == 0 ? 0 : 180) : 90;
            if (hit)
            {
                Vec3d at = motor.Position;
                Vec3d attacker = at + Vec3d.Forward(yaw) * 2.8;
                motor.Knock(copy => CombatRules.VelocityPacket(
                    CombatRules.Knockback(copy, attacker, at, yaw + 180, true, true, random)));
            }
            MovementInput input = kind switch
            {
                "sprint" => new MovementInput(1, 0, false, true, false, yaw),
                "strafe" => new MovementInput(0, (int)(t / 0.4) % 2 == 0 ? 1 : -1, false, true, false, yaw),
                "combo, holding W" => new MovementInput(1, 0, false, true, false, yaw),
                _ => new MovementInput(0, 0, false, true, false, yaw),
            };
            motor.Step(input);
            frames.Add(new Frame(motor.Position, motor.IsTouchingGround, motor.Sprinting ? 1 : 0, yaw, hit));
        }
        return frames;
    }

    /// <summary>
    /// How far our drawing (the game's buffer of states, shown <paramref name="delay"/> in the
    /// past) is from 1.8.9's (where its clicks find the player), in blocks: the average and the
    /// worst 5%, over 3 x 6 x 6 tick phases and every step after the first second.
    /// </summary>
    private static (double Mean, double P95) DistanceFromMinecraft(List<Frame> frames, double delay)
    {
        var distances = new List<double>();
        for (int report = 0; report < N; report++)
        {
            for (int s = 0; s < 6; s++)
            {
                for (int v = 0; v < 6; v++)
                {
                    var view = new McView(frames[0].Feet, frames[0].Yaw, (s + 0.5) / 6 * 0.05, (v + 0.3) / 6 * 0.05);
                    var buffer = new SnapshotBuffer();
                    double yaw = frames[0].Yaw;
                    for (int k = 0; k < frames.Count; k++)
                    {
                        Frame f = frames[k];
                        double t = (k + 1) * Step;
                        yaw += Math.IEEERemainder(f.Yaw - yaw, 360);
                        if (f.Hit)
                        {
                            view.Hit(t - Step);
                        }
                        if (k % N == report)
                        {
                            view.Report(t, f.Feet, f.OnGround, yaw, 0, f.Flags);
                        }
                        buffer.Push(t, new PlayerState(f.Feet.ToGodot(), 0, 0, PlayerFlags.None, 20));
                        double click = t + Step / 2;
                        view.Advance(click);
                        if (k >= Tuning.SimRate)
                        {
                            Vec3d ours = Vec3d.From(buffer.Sample(click - delay).Position);
                            distances.Add((ours - view.Hitbox).Length);
                        }
                    }
                }
            }
        }
        distances.Sort();
        return (distances.Average(), distances[(int)(distances.Count * 0.95)]);
    }

    public void TestFollowsMinecraftsDrawing()
    {
        // We show others a fixed 175 ms in the past; 1.8.9 shows them 100 to 250 ms in the past
        // depending on tick timing, glides each update in over 3 ticks and rounds to 1/32 block.
        // Measured (blocks off, average and worst 5%): running 0.11 (0.27), strafing 0.09 (0.19),
        // comboed holding W 0.26 (0.62), comboed letting it carry 0.29 (0.68); drawing them as
        // fresh as possible instead is 0.9, 0.4, 1.3 and 1.7 off.
        foreach (string kind in new[] { "sprint", "strafe", "combo, holding W", "combo, no keys" })
        {
            List<Frame> frames = Path(kind);
            var (mean, p95) = DistanceFromMinecraft(frames, Tuning.McHeadStart);
            var (freshMean, _) = DistanceFromMinecraft(frames, 1.0 / Tuning.SimRate);
            Check(mean < 0.3 && p95 < 0.7, $"{kind}: drawn {mean:0.00} blocks from where 1.8.9 draws them (95% within {p95:0.00})");
            Check(mean < freshMean / 4, $"{kind}: far closer than drawing them fresh ({mean:0.00} against {freshMean:0.00})");
        }
    }

    // --- The pieces ---------------------------------------------------------------------------

    public void TestViewDelay()
    {
        const double headStart = 0.175;
        CheckNear(HeadStart.ViewDelay(headStart, owned: true, 0), 0.175, 1e-12, "our bots: the head start");
        CheckNear(HeadStart.ViewDelay(0, owned: true, 0), Step, 1e-12, "with none, a tick, to move smoothly between ticks");
        CheckNear(HeadStart.ViewDelay(headStart, owned: false, 0.06), 0.175, 1e-12, "a remote player's network delay counts toward it");
        CheckNear(HeadStart.ViewDelay(headStart, owned: false, 0.23), 0.23, 1e-12, "until it needs more");
        CheckNear(HeadStart.ViewDelay(0, owned: false, 0.06), 0.06, 1e-12, "no head start: as fresh as their updates allow (before 0.4.8)");
        CheckNear(HeadStart.MaxViewAge(headStart), 0.175, 1e-12, "hits on the delayed view count");
        CheckNear(HeadStart.MaxViewAge(0), Tuning.LagCap, 1e-12, "with a small head start, the lag cap as before");
        CheckNear(HeadStart.MaxViewAge(Tuning.DefaultHeadStart), 0.150, 1e-12, "at the default head start (75 ms), the lag cap: 150 ms");
        CheckNear(HeadStart.MaxViewAge(Tuning.McHeadStart), 0.175, 1e-12, "at 1.8.9's, the head start: 175 ms");
    }

    public void TestSameDelayAtAnyPing()
    {
        // A remote player's updates arrive this late (one way, with a little jitter): they're
        // shown 175 ms in the past all the same, until their connection alone needs more.
        foreach ((double oneWay, double expected) in new[] { (0.0, 0.175), (0.05, 0.175), (0.12, 0.175), (0.2, 0.237) })
        {
            var buffer = new SnapshotBuffer();
            var random = new Random(2);
            for (int k = 0; k < 600; k++)
            {
                double sent = k * Step;
                buffer.Push(sent, default);
                buffer.NoteArrivalAge(oneWay + random.NextDouble() * 0.01);
            }
            double delay = HeadStart.ViewDelay(Tuning.McHeadStart, owned: false, buffer.RenderDelay);
            CheckNear(delay, expected, 0.01, $"one-way delay {oneWay * 1000:0} ms");
        }
    }

    public void TestTickClockStampsEvenly()
    {
        var clock = new TickClock(Step);
        var stamps = new List<double>();
        // Frames at 30 a second: two ticks run back to back, each reading the same clock.
        for (int frame = 0; frame < 120; frame++)
        {
            double now = 10 + frame / 30.0;
            stamps.Add(clock.Tick(now));
            stamps.Add(clock.Tick(now + 0.0002));
        }
        double worst = 0;
        for (int i = 60; i < stamps.Count; i++)
        {
            worst = Math.Max(worst, Math.Abs(stamps[i] - stamps[i - 1] - Step));
        }
        Check(worst < 0.003, $"ticks stay about a tick apart when they run in bursts (off by up to {worst * 1000:0.0} ms)");
        CheckNear(stamps[^1], 10 + 119 / 30.0 + Step, 0.01, "and keep up with the clock");
        CheckNear(clock.Tick(50), 50, 1e-12, "after a stall, it jumps to the clock");
    }

    // --- What it does to a fight ------------------------------------------------------------------

    /// <summary>
    /// After a sprint hit at full reach on a player who lets the knockback carry them, with the
    /// attacker holding W: the first moments (seconds after the hit) each sees the other in reach,
    /// the attacker once hit protection is over. Both see the other through
    /// <paramref name="headStart"/>, as the game's buffers show them.
    /// </summary>
    private static (double Attacker, double Victim) BackInReach(double headStart)
    {
        var world = FlatWorld();
        var attacker = new PlayerMotor(world, new Vec3d(48.5, FloorY, 60.5));
        var victim = new PlayerMotor(world, new Vec3d(48.5, FloorY, 48.5));
        var attackerSeen = new SnapshotBuffer();
        var victimSeen = new SnapshotBuffer();
        double delay = HeadStart.ViewDelay(headStart, owned: true, 0);
        double t = 0;
        Vec3d Seen(SnapshotBuffer b, double now) => Vec3d.From(b.Sample(now - delay).Position);
        void Tick(double forward)
        {
            attacker.Step(new MovementInput(forward, 0, false, true, false, 0));
            victim.Step(new MovementInput(0, 0, false, true, false, 180));
            t += Step;
            attackerSeen.Push(t, new PlayerState(attacker.Position.ToGodot(), 0, 0, PlayerFlags.None, 20));
            victimSeen.Push(t, new PlayerState(victim.Position.ToGodot(), 0, 0, PlayerFlags.None, 20));
        }
        double Reach(Vec3d from, Vec3d to) => HitScan.Hitbox(to).DistanceTo(from + new Vec3d(0, Tuning.EyeHeight, 0));
        for (int s = 0; s < 30; s++)
        {
            Tick(0);
        }
        while (Reach(attacker.Position, Seen(victimSeen, t)) > Tuning.Reach)
        {
            Tick(1);
        }
        Vec3d at = victim.Position;
        Vec3d from = attacker.Position;
        victim.Knock(copy => CombatRules.VelocityPacket(CombatRules.Knockback(copy, from, at, 0, true, true, new Random(1))));
        attacker.OnHitLanded(accepted: true);
        double hitAt = t;
        double attackerIn = double.NaN;
        double victimIn = double.NaN;
        while (t - hitAt < 2 && (double.IsNaN(attackerIn) || double.IsNaN(victimIn)))
        {
            Tick(1);
            if (double.IsNaN(attackerIn) && t - hitAt >= 0.45 && Reach(attacker.Position, Seen(victimSeen, t)) <= Tuning.Reach)
            {
                attackerIn = t - hitAt;
            }
            if (double.IsNaN(victimIn) && t - hitAt >= 0.1 && Reach(victim.Position, Seen(attackerSeen, t)) <= Tuning.Reach)
            {
                victimIn = t - hitAt;
            }
        }
        return (attackerIn, victimIn);
    }

    public void TestTheChaserGetsTheHeadStart()
    {
        var (attacker, victim) = BackInReach(0);
        CheckNear(attacker, victim, 1.5 * Step, $"drawn fresh, both are back in reach together ({attacker:0.000} s, {victim:0.000} s): a click race");
        (attacker, victim) = BackInReach(Tuning.McHeadStart);
        Check(victim - attacker > 0.15,
            $"with 1.8.9's head start the attacker gets back in reach first ({attacker:0.000} s, the victim {victim:0.000} s)");
    }
}
