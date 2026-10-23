using System;
using Godot;
using UhcPvp.Combat;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

/// <summary>
/// Jumping around a knockback (a "jump reset"), against 1.8.9 (<see cref="McReference"/> with
/// <see cref="McServerCopy"/>). 1.8.9 applies a knockback's velocity packet between ticks and
/// reads the jump key once per tick, so a jump near a knockback has three outcomes:
/// - on time: jumping on the tick the knockback lands. The jump's lift and sprint-jump boost
///   replace part of it: the shortest flight there is.
/// - early: a tick or more before. Knocked back mid-air, by a server copy that saw the jump: the
///   longest flight.
/// - late: airborne by then, so no jump at all.
/// Our jumps start the instant they're pressed. These tests press jump on every 1/60 s step from
/// two ticks before to two ticks after the knockback, with the knockback arriving at every point
/// of a tick, and check that every case follows exactly one of those 1.8.9 flights.
/// </summary>
public class JumpResetTests : TestCase
{
    private const int N = 3;
    private const double FloorY = 4;
    private static readonly Vec3d Start = new(32.5, FloorY, 32.5);
    /// <summary>Ticks of running before the knockback.</summary>
    private const int SettleTicks = 15;
    private const int FlightTicks = 20;

    private static VoxelWorld FlatWorld()
    {
        var world = new VoxelWorld(new Vector3I(64, 24, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, 3, 63), Block.Stone);
        return world;
    }

    /// <summary>The victim faces the attacker, who stands to its +Z (yaw 180 faces +Z), sprint key held.</summary>
    private static MovementInput Keys(double forward, double strafe, bool jump) =>
        new(forward, strafe, jump, true, false, 180);

    private static readonly MovementInput Idle = new(0, 0, false, false, false, 180);

    /// <summary>A sprint hit from 3 blocks to +Z, attacker facing -Z, built from the server's copy.</summary>
    private static Vec3d Knockback(Vec3d copy, Vec3d victim) => CombatRules.VelocityPacket(
        CombatRules.Knockback(copy, victim + new Vec3d(0, 0, 3), victim, 0, true, true, new Random(1)));

    /// <summary>
    /// 1.8.9: the victim runs <see cref="SettleTicks"/> ticks with its keys, jumping on the tick
    /// <paramref name="earlyTicks"/> before the knockback (if any); the knockback is built from the
    /// server's copy after the last report; then it flies with the same keys, jumping on the first
    /// tick if <paramref name="onTime"/>. Positions after each tick, relative to where the
    /// knockback landed.
    /// </summary>
    private static Vec3d[] Reference(double forward, double strafe, int earlyTicks, bool onTime)
    {
        VoxelWorld world = FlatWorld();
        var mc = new McReference(world, Start);
        var server = new McServerCopy(world, Start);
        void Tick(MovementInput input)
        {
            mc.Tick(input);
            server.Report(mc.Position, mc.OnGround, mc.Sprinting, input.Yaw);
        }
        for (int t = 0; t < 5; t++)
        {
            Tick(Idle);
        }
        for (int t = 0; t < SettleTicks; t++)
        {
            Tick(Keys(forward, strafe, earlyTicks > 0 && t == SettleTicks - earlyTicks));
        }
        Vec3d origin = mc.Position;
        mc.ReceiveVelocity(Knockback(server.Motion, origin));
        var path = new Vec3d[FlightTicks];
        for (int i = 0; i < FlightTicks; i++)
        {
            Tick(Keys(forward, strafe, onTime && i == 0));
            path[i] = mc.Position - origin;
        }
        return path;
    }

    /// <summary>
    /// Ours, at 60 steps a second: the same run, with the knockback arriving just before step
    /// <paramref name="arrival"/> and jump held for one Minecraft tick from step
    /// <paramref name="press"/> (steps counted from the end of settling on the floor). Returns the
    /// position before every step.
    /// </summary>
    private static Vec3d[] Ours(double forward, double strafe, int arrival, int press)
    {
        var ours = new PlayerMotor(FlatWorld(), Start);
        for (int s = 0; s < 5 * N; s++)
        {
            ours.Step(Idle);
        }
        int steps = arrival + 3 * N + (FlightTicks + 2) * N;
        var positions = new Vec3d[steps];
        for (int s = 0; s < steps; s++)
        {
            if (s == arrival)
            {
                Vec3d victim = ours.Position;
                ours.Knock(copy => Knockback(copy, victim));
            }
            positions[s] = ours.Position;
            ours.Step(Keys(forward, strafe, s >= press && s < press + N));
        }
        return positions;
    }

    /// <summary>
    /// The 1.8.9 outcome a press should give, and the step the knockback lands on. Knockback waits
    /// for the end of the Minecraft tick in progress, as 1.8.9 applies a velocity packet between
    /// ticks. A jump starts a tick the instant it's pressed, so a knockback arriving during the
    /// jump's first tick lands after it (early); a jump pressed between the knockback arriving and
    /// that tick ending starts the tick there, with the knockback (on time); and a jump can't
    /// restart a tick that knockback started, as 1.8.9 reads the jump key only at the next tick
    /// (late: airborne by then).
    /// </summary>
    private static (int Lands, int EarlyTicks, bool OnTime) Expected(int arrival, int press)
    {
        if (press < arrival)
        {
            int ticks = (arrival - press + N - 1) / N;
            return (press + ticks * N, ticks, false);
        }
        // Without a jump, Minecraft ticks start every N steps from the end of settling.
        int tickEnd = SettleTicks * N + (arrival - SettleTicks * N + N - 1) / N * N;
        return press <= tickEnd ? (press, 0, true) : (tickEnd, 0, false);
    }

    private void CheckEveryPress(string name, double forward, double strafe)
    {
        for (int phase = 0; phase < N; phase++)
        {
            int arrival = SettleTicks * N + phase;
            for (int offset = -2 * N; offset <= 2 * N; offset++)
            {
                (int lands, int early, bool onTime) = Expected(arrival, arrival + offset);
                Vec3d[] reference = Reference(forward, strafe, early, onTime);
                Vec3d[] ours = Ours(forward, strafe, arrival, arrival + offset);
                double worst = 0;
                for (int i = 0; i < FlightTicks; i++)
                {
                    worst = Math.Max(worst, ((ours[lands + (i + 1) * N] - ours[lands]) - reference[i]).Length);
                }
                string outcome = onTime ? "on time" : early > 0 ? $"{early} tick(s) early" : "late";
                Check(worst < 1e-9,
                    $"{name}, knockback {phase} step(s) into a tick, jump {offset * 1000 / 60} ms after it: "
                    + $"should follow 1.8.9's {outcome} flight, off by {worst:0.000} blocks");
            }
        }
    }

    public void TestJumpResetWhileSprintingAtTheAttacker() => CheckEveryPress("holding W", 1, 0);

    public void TestJumpResetStandingStill() => CheckEveryPress("no keys", 0, 0);

    public void TestJumpResetWhileStrafing() => CheckEveryPress("holding A", 0, 1);

    public void TestJumpResetDistances()
    {
        // Travel in the 500 ms after a sprint hit, holding W: the numbers in docs/combat.md.
        static double Travel(int early, bool onTime) => Reference(1, 0, early, onTime)[9].HorizontalLength;
        CheckNear(Travel(0, true), 1.65, 0.005, "on time");
        CheckNear(Travel(1, false), 4.34, 0.005, "a tick early");
        CheckNear(Travel(0, false), 2.54, 0.005, "late, or no jump");
    }
}
