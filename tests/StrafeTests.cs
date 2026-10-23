using System;
using Godot;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

/// <summary>
/// Our strafing twists (match rules, not 1.8.9). Faster strafing: sideways speed goes up on the
/// ground, top speed doesn't. Snappier strafing (grip): starting, stopping and switching sideways
/// get quicker, top speeds don't change, knockback stays whole. Both leave the air alone, and our
/// 60 ticks/s movement still lands exactly where the 20 ticks/s reference does with the same rules.
/// </summary>
public class StrafeTests : TestCase
{
    private const int N = 3;
    private const double FloorY = 4;
    private const double StraightSprint = 5.612; // blocks/s, 1.8.9's
    private const double Walk = 4.317;

    private static readonly double[] Strengths = { 1.15, 1.3, 1.5 };

    private static VoxelWorld FlatWorld()
    {
        var world = new VoxelWorld(new Vector3I(64, 24, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, (int)FloorY - 1, 63), Block.Stone);
        return world;
    }

    private static readonly Vec3d Start = new(32.5, FloorY, 32.5);

    private static MovementInput Keys(double forward, double strafe, bool jump = false, double yaw = 0) =>
        new(forward, strafe, jump, true, false, yaw);

    private static (McReference Mc, PlayerMotor Ours) Pair(double strafeSpeed, Vec3d? start = null, double grip = 0)
    {
        VoxelWorld world = FlatWorld();
        var mc = new McReference(world, start ?? Start) { StrafeSpeed = strafeSpeed, StrafeGrip = grip };
        var ours = new PlayerMotor(world, start ?? Start) { StrafeSpeed = strafeSpeed, StrafeGrip = grip };
        for (int t = 0; t < 5; t++)
        {
            mc.Tick(Keys(0, 0));
            for (int s = 0; s < N; s++)
            {
                ours.Step(Keys(0, 0));
            }
        }
        return (mc, ours);
    }

    /// <summary>Horizontal speed after 3 seconds of holding the keys on flat ground, in blocks/s.</summary>
    private static double SteadySpeed(double strafeSpeed, MovementInput keys, double grip = 0)
    {
        var (_, ours) = Pair(strafeSpeed, grip: grip);
        for (int s = 0; s < 180; s++)
        {
            ours.Step(keys);
        }
        return ours.HorizontalSpeed;
    }

    /// <summary>Sideways speed (relative to facing yaw 0, so along X) after 3 seconds, in blocks/s.</summary>
    private static (double Forward, double Sideways) SteadyVelocity(double strafeSpeed, MovementInput keys)
    {
        var (_, ours) = Pair(strafeSpeed);
        for (int s = 0; s < 180; s++)
        {
            ours.Step(keys);
        }
        // Yaw 0 faces -Z; left is -X.
        return (-ours.LastMove.Z * Tuning.SimRate, Math.Abs(ours.LastMove.X) * Tuning.SimRate);
    }

    public void TestOffIsExactlyMinecraft()
    {
        var accel = new Vec3d(0.03, -0.1, -0.07);
        CheckEqual(PlayerMotor.BoostStrafe(accel, 37, 1.0, 0.1), accel, "1.0 leaves the acceleration alone");
        // A motor on its own moves like 1.8.9 (the parity tests rely on it); a match sets the twists.
        CheckEqual(new PlayerMotor(FlatWorld(), Start).StrafeSpeed, Tuning.McStrafeSpeed, "players start with 1.8.9's strafing");
        CheckEqual(new PlayerMotor(FlatWorld(), Start).StrafeGrip, Tuning.McStrafeGrip, "and 1.8.9's snappiness, not the match default");
    }

    public void TestFasterStrafingMatchesItsReferenceExactly()
    {
        // Our 60 ticks/s movement with the rule lands exactly where the 20 ticks/s reference
        // with the same rule does, at every Minecraft tick: walking, sprinting, jumping and turning.
        var random = new Random(4242);
        foreach (double k in Strengths)
        {
            foreach (var (name, input) in new (string, Func<int, MovementInput>)[]
            {
                ("W+A", _ => Keys(1, 1, yaw: 20)),
                ("A only", _ => Keys(0, 1, yaw: -60)),
                ("S+D", _ => Keys(-1, -1, yaw: 135)),
                ("W+D bunny hops", _ => Keys(1, -1, jump: true, yaw: 10)),
                ("A-D dodging", t => Keys(1, t % 6 < 3 ? 1 : -1, yaw: t * 3.0)),
            })
            {
                CheckMatches(k, input, 60, name);
            }
            var inputs = new MovementInput[150];
            for (int t = 0; t < inputs.Length; t++)
            {
                inputs[t] = new MovementInput(random.Next(-1, 2), random.Next(-1, 2), random.Next(5) == 0,
                    true, random.Next(8) == 0, random.NextDouble() * 360 - 180);
            }
            CheckMatches(k, t => inputs[t], inputs.Length, "random inputs");
        }
    }

    private void CheckMatches(double k, Func<int, MovementInput> input, int ticks, string name, double grip = 0)
    {
        var (mc, ours) = Pair(k, grip: grip);
        double worst = 0;
        for (int t = 0; t < ticks; t++)
        {
            MovementInput step = input(t);
            mc.Tick(step);
            for (int s = 0; s < N; s++)
            {
                ours.Step(step);
            }
            Vec3d d = mc.Position - ours.Position;
            worst = Math.Max(worst, Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z));
        }
        Check(worst < 1e-9, $"{name} at {MatchRules.Percent(k)}, grip {MatchRules.Percent(grip)}: off by {worst:E2} blocks");
    }

    public void TestSnappierStrafingMatchesItsReferenceExactly()
    {
        var random = new Random(777);
        foreach (double grip in new[] { 0.3, 0.6, 1.0 })
        {
            foreach (double k in new[] { 1.0, 1.3 })
            {
                CheckMatches(k, t => Keys(1, t % 6 < 3 ? 1 : -1, yaw: 15), 60, "W with A-D dodging", grip);
                CheckMatches(k, t => Keys(0, t % 8 < 4 ? 1 : -1, yaw: -30), 60, "A-D alone", grip);
                CheckMatches(k, t => Keys(t % 10 < 5 ? 1 : 0, t % 7 < 3 ? 1 : 0, jump: t % 13 == 0, yaw: t * 5.0), 80, "starting, stopping, jumping, turning", grip);
                var inputs = new MovementInput[150];
                for (int t = 0; t < inputs.Length; t++)
                {
                    inputs[t] = new MovementInput(random.Next(-1, 2), random.Next(-1, 2), random.Next(5) == 0,
                        true, random.Next(8) == 0, random.NextDouble() * 360 - 180);
                }
                CheckMatches(k, t => inputs[t], inputs.Length, "random inputs", grip);
            }
        }
    }

    /// <summary>Minecraft ticks after switching from full A to D until moving right at 90% of full speed.</summary>
    private static int TicksToSwitch(double grip)
    {
        var (_, ours) = Pair(1.0, grip: grip);
        for (int s = 0; s < 120; s++)
        {
            ours.Step(Keys(0, 1)); // full speed left (-X)
        }
        double full = ours.LastMove.X * -Tuning.SimRate;
        for (int t = 1; t <= 20; t++)
        {
            for (int s = 0; s < N; s++)
            {
                ours.Step(Keys(0, -1));
            }
            if (ours.LastMove.X * Tuning.SimRate >= 0.9 * full)
            {
                return t;
            }
        }
        return int.MaxValue;
    }

    public void TestSnappierStrafingSwitchesSooner()
    {
        int plain = TicksToSwitch(0);
        CheckEqual(plain, 5, "1.8.9 takes a quarter second (5 ticks) to switch from A to D");
        int last = plain;
        foreach (double grip in new[] { 0.25, 0.5, 0.75, 1.0 })
        {
            int ticks = TicksToSwitch(grip);
            Check(ticks <= last, $"grip {MatchRules.Percent(grip)}: {ticks} ticks, no slower than {last}");
            last = ticks;
        }
        Check(TicksToSwitch(0.5) < plain, $"grip 50% switches sooner ({TicksToSwitch(0.5)} ticks)");
        CheckEqual(TicksToSwitch(1.0), 1, "grip 100% switches at once");
    }

    public void TestSnappierStrafingKeepsTopSpeeds()
    {
        for (int forward = -1; forward <= 1; forward++)
        {
            for (int strafe = -1; strafe <= 1; strafe++)
            {
                if (forward == 0 && strafe == 0)
                {
                    continue;
                }
                MovementInput keys = Keys(forward, strafe);
                CheckNear(SteadySpeed(1.3, keys, grip: 0.8), SteadySpeed(1.3, keys), 1e-6, $"forward {forward}, strafe {strafe}");
            }
        }
    }

    public void TestSnappierStrafingKeepsKnockbackWhole()
    {
        // Standing still, holding A, knocked sideways (to the right): 1.8.9's knockback path at
        // any grip, because the tick after a velocity packet is left alone and then you're airborne.
        Vec3d[] paths = new Vec3d[2];
        double[] grips = { 0.0, 1.0 };
        for (int i = 0; i < 2; i++)
        {
            var (_, ours) = Pair(1.0, grip: grips[i]);
            ours.SetMcMotion(new Vec3d(0.4, 0.4, 0));
            for (int s = 0; s < 30; s++)
            {
                ours.Step(Keys(0, 1));
            }
            paths[i] = ours.Position;
        }
        CheckEqual(paths[1], paths[0], "same knockback path with grip 100%");
        // And the reference agrees on the rule.
        var (mc, motor) = Pair(1.0, grip: 1.0);
        mc.ReceiveVelocity(new Vec3d(0.4, 0.4, 0));
        motor.SetMcMotion(new Vec3d(0.4, 0.4, 0));
        for (int t = 0; t < 10; t++)
        {
            mc.Tick(Keys(0, 1));
            for (int s = 0; s < N; s++)
            {
                motor.Step(Keys(0, 1));
            }
        }
        Vec3d d = mc.Position - motor.Position;
        Check(Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z) < 1e-9, "matches the reference after knockback");
    }

    public void TestNeverFasterThanAStraightSprint()
    {
        // Every key combination, at every slider step: no faster than 1.8.9 allowed for those
        // keys or a straight sprint, whichever is more (W+A/D is already 2% faster in 1.8.9).
        for (double k = 1.0; k <= Tuning.MaxStrafeSpeed + 1e-9; k += Tuning.StrafeSpeedStep)
        {
            for (int forward = -1; forward <= 1; forward++)
            {
                for (int strafe = -1; strafe <= 1; strafe++)
                {
                    if (forward == 0 && strafe == 0)
                    {
                        continue;
                    }
                    MovementInput keys = Keys(forward, strafe);
                    double minecraft = SteadySpeed(1.0, keys);
                    double ours = SteadySpeed(k, keys);
                    Check(ours <= Math.Max(minecraft, StraightSprint) + 0.002,
                        $"forward {forward}, strafe {strafe} at {MatchRules.Percent(k)}: {ours:F3} b/s (1.8.9 {minecraft:F3})");
                }
            }
        }
    }

    public void TestSidewaysGetsFaster()
    {
        // A or D alone: walking speed times the strength, up to a straight sprint (at about 130%).
        foreach (var (k, expected) in new[] { (1.15, Walk * 1.15), (1.3, StraightSprint), (1.5, StraightSprint) })
        {
            CheckNear(SteadySpeed(k, Keys(0, 1)), expected, 0.01, $"A alone at {MatchRules.Percent(k)}");
        }
        // W+A: more sideways, less forward, same total.
        var (plainForward, plainSideways) = SteadyVelocity(1.0, Keys(1, 1));
        double lastSideways = plainSideways;
        foreach (double k in Strengths)
        {
            var (forward, sideways) = SteadyVelocity(k, Keys(1, 1));
            Check(sideways > lastSideways + 0.1, $"W+A at {MatchRules.Percent(k)}: sideways {sideways:F2} b/s, up from {lastSideways:F2}");
            Check(forward < plainForward, $"W+A at {MatchRules.Percent(k)}: forward {forward:F2} b/s gives way (1.8.9 {plainForward:F2})");
            lastSideways = sideways;
        }
        // Straight ahead and straight back don't change.
        CheckNear(SteadySpeed(1.5, Keys(1, 0)), SteadySpeed(1.0, Keys(1, 0)), 1e-9, "W alone");
        CheckNear(SteadySpeed(1.5, Keys(-1, 0)), SteadySpeed(1.0, Keys(-1, 0)), 1e-9, "S alone");
    }

    public void TestAirMovementStaysMinecraft()
    {
        // In the air (knockback, combos) strafing is 1.8.9's at any strength.
        var high = new Vec3d(32.5, FloorY + 12, 32.5);
        var plain = new PlayerMotor(FlatWorld(), high);
        var boosted = new PlayerMotor(FlatWorld(), high) { StrafeSpeed = 1.5, StrafeGrip = 1.0 };
        for (int s = 0; s < 30; s++)
        {
            plain.Step(Keys(1, 1, yaw: 40));
            boosted.Step(Keys(1, 1, yaw: 40));
        }
        Check(!boosted.IsTouchingGround, "still falling");
        CheckEqual(boosted.Position, plain.Position, "same path through the air");
    }
}
