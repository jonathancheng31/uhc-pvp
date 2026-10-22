using System;
using Godot;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

/// <summary>
/// Checks <see cref="PlayerMotor"/> (60 ticks/s) against <see cref="McReference"/> (Minecraft
/// 1.8.9 at 20 ticks/s). Ground movement must match exactly at every Minecraft tick; anything
/// involving take-off and landing must be within a small tolerance.
/// </summary>
public class MovementTests : TestCase
{
    private const int N = 3;
    private const double FloorY = 4;

    private static MovementInput Idle(double yaw = 0) => new(0, 0, false, false, false, yaw);
    private static MovementInput Walk(double yaw = 0) => new(1, 0, false, false, false, yaw);
    private static MovementInput Sprint(double yaw = 0) => new(1, 0, false, true, false, yaw);

    /// <summary>A 64x64 stone floor with its top at y = 4.</summary>
    private static VoxelWorld FlatWorld()
    {
        var world = new VoxelWorld(new Vector3I(64, 24, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, (int)FloorY - 1, 63), Block.Stone);
        return world;
    }

    private static readonly Vec3d Start = new(32.5, FloorY, 32.5);

    /// <summary>A reference player and our player side by side, both settled on the floor.</summary>
    private static (McReference Mc, PlayerMotor Ours) Pair(VoxelWorld world, Vec3d? start = null)
    {
        var mc = new McReference(world, start ?? Start);
        var ours = new PlayerMotor(world, start ?? Start);
        for (int t = 0; t < 5; t++)
        {
            mc.Tick(Idle());
            for (int s = 0; s < N; s++)
            {
                ours.Step(Idle());
            }
        }
        return (mc, ours);
    }

    private static void StepMcTick(McReference mc, PlayerMotor ours, MovementInput input)
    {
        mc.Tick(input);
        for (int s = 0; s < N; s++)
        {
            ours.Step(input);
        }
    }

    private static double Distance(Vec3d a, Vec3d b)
    {
        Vec3d d = a - b;
        return Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
    }

    private void CheckMatchesEveryTick(McReference mc, PlayerMotor ours, int ticks, Func<int, MovementInput> input, string what)
    {
        double worst = 0;
        int worstTick = -1;
        for (int t = 0; t < ticks; t++)
        {
            StepMcTick(mc, ours, input(t));
            double error = Distance(mc.Position, ours.Position);
            if (error > worst)
            {
                worst = error;
                worstTick = t;
            }
        }
        Check(worst < 1e-9, $"{what}: off by {worst:E2} blocks at MC tick {worstTick}");
    }

    public void TestGroundMovementMatchesMinecraftExactly()
    {
        foreach (var (name, input) in new (string, MovementInput)[]
        {
            ("walk", Walk(30)),
            ("sprint", Sprint(30)),
            ("sneak", new MovementInput(1, 0, false, false, true, 30)),
            ("back", new MovementInput(-1, 0, false, false, false, -75)),
            ("strafe", new MovementInput(0, 1, false, false, false, 10)),
            ("diagonal sprint", new MovementInput(1, -1, false, true, false, 135)),
        })
        {
            var (mc, ours) = Pair(FlatWorld());
            CheckMatchesEveryTick(mc, ours, 40, _ => input, name);
        }
    }

    public void TestChangingInputsMatchMinecraftExactly()
    {
        // Strafing back and forth, turning, starting and stopping: everything a duel does on
        // the ground. Inputs change on Minecraft tick boundaries.
        var random = new Random(1889);
        var (mc, ours) = Pair(FlatWorld());
        var inputs = new MovementInput[200];
        for (int t = 0; t < inputs.Length; t++)
        {
            inputs[t] = new MovementInput(
                random.Next(-1, 2), random.Next(-1, 2), false, random.Next(3) > 0, random.Next(6) == 0,
                random.NextDouble() * 360 - 180);
        }
        CheckMatchesEveryTick(mc, ours, inputs.Length, t => inputs[t], "random ground inputs");
    }

    public void TestStoppingDistanceMatchesMinecraft()
    {
        var (mc, ours) = Pair(FlatWorld());
        CheckMatchesEveryTick(mc, ours, 60, t => t < 40 ? Sprint() : Idle(), "sprint then release");
        CheckNear(ours.HorizontalSpeed, 0, 1e-9, "fully stopped");
    }

    public void TestTopSpeedsMatchMinecraft()
    {
        foreach (var (name, input, expected) in new (string, MovementInput, double)[]
        {
            ("walk", Walk(), 4.317),
            ("sprint", Sprint(), 5.612),
            ("sneak", new MovementInput(1, 0, false, false, true, 0), 1.295),
        })
        {
            var (_, ours) = Pair(FlatWorld());
            for (int s = 0; s < 120; s++)
            {
                ours.Step(input);
            }
            CheckNear(ours.HorizontalSpeed, expected, 0.001, $"{name} speed (blocks/s)");
        }
    }

    public void TestJumpReachesMinecraftHeightAndLandsOnTime()
    {
        var (mc, ours) = Pair(FlatWorld());
        double mcPeak = 0;
        double ourTickPeak = 0;
        double ourPeak = 0;
        int mcLand = -1;
        int ourLand = -1;
        for (int t = 0; t < 30; t++)
        {
            MovementInput input = new(0, 0, t == 0, false, false, 0);
            mc.Tick(input);
            mcPeak = Math.Max(mcPeak, mc.Position.Y - FloorY);
            if (mcLand < 0 && t > 0 && mc.OnGround)
            {
                mcLand = t + 1;
            }
            for (int s = 0; s < N; s++)
            {
                ours.Step(input);
                ourPeak = Math.Max(ourPeak, ours.Position.Y - FloorY);
                if (ourLand < 0 && t > 0 && ours.IsTouchingGround)
                {
                    ourLand = t + 1;
                }
            }
            ourTickPeak = Math.Max(ourTickPeak, ours.Position.Y - FloorY);
        }
        CheckNear(mcPeak, 1.2492, 0.0001, "Minecraft's jump height (sanity check of the reference)");
        CheckNear(ourTickPeak, mcPeak, 1e-9, "jump height at Minecraft ticks");
        // Between Minecraft's ticks we follow the smooth arc, which peaks a hair higher.
        Check(ourPeak - mcPeak is >= 0 and < 0.01, $"peak between ticks is within 1/100 block (was +{ourPeak - mcPeak:F4})");
        CheckEqual(ourLand, mcLand, "lands during the same Minecraft tick");
    }

    public void TestJumpsAndBunnyHopsMatchMinecraftExactly()
    {
        var (mc, ours) = Pair(FlatWorld());
        CheckMatchesEveryTick(mc, ours, 15, t => new MovementInput(0, 0, t == 0, false, false, 0), "standing jump");
        CheckMatchesEveryTick(mc, ours, 100, _ => new MovementInput(1, 0, true, true, false, 20), "sprint bunny hops");
        CheckMatchesEveryTick(mc, ours, 60, t => new MovementInput(t % 7 < 4 ? 1 : 0, t % 5 < 2 ? 1 : -1, t % 13 < 9, true, false, t * 4.0), "jumping while strafing and turning");
    }

    public void TestSprintJumpingKeepsMinecraftPace()
    {
        // A long strip: 10 s of sprint-jumping covers about 70 blocks.
        var world = new VoxelWorld(new Vector3I(16, 24, 256));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(15, (int)FloorY - 1, 255), Block.Stone);
        var (mc, ours) = Pair(world, new Vec3d(8.5, FloorY, 250.5));
        Vec3d mcStart = mc.Position;
        Vec3d ourStart = ours.Position;
        var input = new MovementInput(1, 0, true, true, false, 0);
        for (int t = 0; t < 200; t++)
        {
            StepMcTick(mc, ours, input);
        }
        double mcSpeed = Distance(mc.Position, mcStart) / 10.0;
        double ourSpeed = Distance(ours.Position, ourStart) / 10.0;
        CheckNear(ourSpeed / mcSpeed, 1.0, 0.01, $"sprint-jump pace (Minecraft {mcSpeed:F3} b/s, ours {ourSpeed:F3} b/s)");
    }

    public void TestSingleSprintJumpCoversMinecraftDistance()
    {
        var (mc, ours) = Pair(FlatWorld());
        for (int t = 0; t < 40; t++)
        {
            StepMcTick(mc, ours, Sprint());
        }
        Vec3d mcTakeoff = mc.Position;
        Vec3d ourTakeoff = ours.Position;
        double mcDistance = 0;
        double ourDistance = 0;
        for (int t = 0; t < 20; t++)
        {
            var input = new MovementInput(1, 0, t == 0, true, false, 0);
            mc.Tick(input);
            if (mcDistance == 0 && t > 0 && mc.OnGround)
            {
                mcDistance = Distance(mc.Position, mcTakeoff);
            }
            for (int s = 0; s < N; s++)
            {
                ours.Step(input);
                if (ourDistance == 0 && t > 0 && ours.OnGround)
                {
                    ourDistance = Distance(ours.Position, ourTakeoff);
                }
            }
        }
        CheckNear(ourDistance / mcDistance, 1.0, 0.04, $"one sprint jump (Minecraft {mcDistance:F3}, ours {ourDistance:F3})");
    }

    public void TestAirControlMatchesMinecraft()
    {
        var (mc, ours) = Pair(FlatWorld());
        for (int t = 0; t < 11; t++)
        {
            // Jump in place, then hold W in the air.
            StepMcTick(mc, ours, new MovementInput(t >= 1 ? 1 : 0, 0, t == 0, false, false, 0));
        }
        double mcDrift = Start.Z - mc.Position.Z;
        double ourDrift = Start.Z - ours.Position.Z;
        CheckNear(ourDrift, mcDrift, 1e-9, "distance drifted forward in the air");
    }

    public void TestSprintChangesInTheAirMatchMinecraftExactly()
    {
        // 1.8.9 sets the air acceleration (0.02, or 0.026 sprinting) after moving, so in the air
        // a sprint change only takes effect a tick later. The sprint key is held throughout, as
        // in our game.
        foreach (var (name, input) in new (string, Func<int, MovementInput>)[]
        {
            ("sprint-jump, then let go of W and strafe", t => new MovementInput(t < 14 ? 1 : 0, t < 14 ? 0 : 1, t == 10, true, false, 20)),
            ("strafe-jump, then press W in the air", t => new MovementInput(t < 9 ? 0 : 1, 1, t == 5, true, false, -40)),
            ("walk-jump, then press sprint in the air", t => new MovementInput(1, 0, t == 3, t >= 6, false, 75)),
            ("bunny hops tapping W every tick", t => new MovementInput(t % 2, -1, true, true, false, 110)),
        })
        {
            var (mc, ours) = Pair(FlatWorld());
            int airChanges = 0;
            double worst = 0;
            int worstTick = -1;
            for (int t = 0; t < 40; t++)
            {
                bool airTick = !mc.OnGround;
                bool wasSprinting = mc.Sprinting;
                StepMcTick(mc, ours, input(t));
                if (airTick && mc.Sprinting != wasSprinting)
                {
                    airChanges++;
                }
                CheckEqual(ours.Sprinting, mc.Sprinting, $"{name}: sprinting at MC tick {t}");
                double error = Distance(mc.Position, ours.Position);
                if (error > worst)
                {
                    worst = error;
                    worstTick = t;
                }
            }
            Check(airChanges > 0, $"{name}: sprint changes in the air ({airChanges} times)");
            Check(worst < 1e-9, $"{name}: off by {worst:E2} blocks at MC tick {worstTick}");
        }
    }

    public void TestRespawnStartsWithoutSprintAirAcceleration()
    {
        // A respawned player is a fresh Minecraft player: its first tick is airborne and uses
        // the walking air acceleration, even if it was sprinting a moment before.
        VoxelWorld world = FlatWorld();
        var (_, ours) = Pair(world);
        for (int s = 0; s < 20 * N; s++)
        {
            ours.Step(Sprint());
        }
        Check(ours.Sprinting, "sprinting before the respawn");
        var spawn = new Vec3d(32.5, FloorY + 3, 32.5);
        ours.Teleport(spawn);
        var mc = new McReference(world, spawn) { Motion = ours.McMotion };
        CheckMatchesEveryTick(mc, ours, 30, _ => Sprint(), "sprinting from a respawn in the air");
    }

    public void TestWallStopsPlayerAndSprint()
    {
        VoxelWorld world = FlatWorld();
        world.Fill(new Vector3I(0, 4, 30), new Vector3I(63, 6, 30), Block.Stone); // wall at z = 30..31
        var (mc, ours) = Pair(world, new Vec3d(32.5, FloorY, 34.5));
        CheckMatchesEveryTick(mc, ours, 40, _ => Sprint(), "sprinting into a wall");
        CheckNear(ours.Position.Z, 31.3, 1e-9, "stops flush against the wall");
        Check(!ours.Sprinting, "hitting a wall stops sprinting");
        Check(ours.CollidedHorizontally, "knows it hit a wall");
    }

    public void TestSlidingAlongWallsMatchesMinecraftExactly()
    {
        // Running into a wall at an angle slides along it. 1.8.9 only notices the hit at the start
        // of the next tick, when it stops the sprint, so the hit tick still moves (and sets the air
        // acceleration) as a sprint. Start distances vary so the first touch falls in each of our
        // three ticks of a Minecraft tick.
        VoxelWorld walls = FlatWorld();
        walls.Fill(new Vector3I(0, 4, 30), new Vector3I(63, 6, 30), Block.Stone); // wall at z = 30..31
        walls.Fill(new Vector3I(10, 4, 31), new Vector3I(10, 6, 63), Block.Stone); // wall at x = 10..11
        VoxelWorld edges = WallEdgesWorld();
        var alongZWall = new Vec3d(32.5, FloorY, 34.5);
        var furtherBack = new Vec3d(32.5, FloorY, 38.5);
        var alongXWall = new Vec3d(14.5, FloorY, 50.5);
        var nearWallEnd = new Vec3d(24.5, FloorY, 34.5);
        foreach (var (name, world, start, ticks, input) in new (string, VoxelWorld, Vec3d, int, Func<int, MovementInput>)[]
        {
            ("sprinting along a wall", walls, alongZWall, 40, _ => Sprint(30)),
            ("sprinting along a wall the other way", walls, alongZWall, 40, _ => Sprint(-50)),
            ("sprinting along a wall holding W and A", walls, alongZWall, 40, _ => new MovementInput(1, 1, false, true, false, -20)),
            ("sprinting along a wall on the X side", walls, alongXWall, 40, _ => Sprint(60)),
            ("sliding, then turning away from the wall", walls, alongZWall, 40, t => Sprint(t < 20 ? 30 : 160)),
            ("sliding off the end of a wall", edges, nearWallEnd, 40, _ => Sprint(30)),
            ("sprint-jumping along a wall", walls, alongZWall, 60, _ => new MovementInput(1, 0, true, true, false, 30)),
            ("sprint-jumping into a wall from further back", walls, furtherBack, 60, _ => new MovementInput(1, 0, true, true, false, 25)),
            ("sprint-jumping along a wall on the X side", walls, alongXWall, 60, _ => new MovementInput(1, 0, true, true, false, 55)),
        })
        {
            var firstHits = new System.Collections.Generic.HashSet<int>();
            bool hitInTheAir = false;
            foreach (double offset in new[] { 0.0, 0.07, 0.13, 0.21, 0.29, 0.34 })
            {
                Vec3d from = start == alongXWall ? start + new Vec3d(offset, 0, 0) : start + new Vec3d(0, 0, offset);
                var (mc, ours) = Pair(world, from);
                string what = $"{name} (start {offset:F2} further)";
                double worst = 0;
                int worstTick = -1;
                bool hit = false;
                for (int t = 0; t < ticks; t++)
                {
                    MovementInput step = input(t);
                    mc.Tick(step);
                    for (int s = 0; s < N; s++)
                    {
                        ours.Step(step);
                        if (!hit && ours.CollidedHorizontally)
                        {
                            hit = true;
                            firstHits.Add(s);
                            hitInTheAir |= !ours.IsTouchingGround;
                        }
                    }
                    CheckEqual(ours.Sprinting, mc.Sprinting, $"{what}: sprinting at MC tick {t}");
                    double error = Distance(mc.Position, ours.Position);
                    if (error > worst)
                    {
                        worst = error;
                        worstTick = t;
                    }
                }
                Check(hit, $"{what}: reaches the wall");
                Check(worst < 1e-9, $"{what}: off by {worst:E2} blocks at MC tick {worstTick}");
            }
            CheckEqual(firstHits.Count, N, $"{name}: first touches cover all {N} of our ticks in a Minecraft tick");
            if (name.Contains("further back", StringComparison.Ordinal))
            {
                Check(hitInTheAir, $"{name}: some runs touch the wall in mid-air");
            }
        }
    }

    public void TestJumpRightAfterAWallHitHasNoSprintBoost()
    {
        // A jump can start partway through a Minecraft tick, and starts a new one there. Like any
        // tick start, it first checks whether the last move hit a wall, so jumping right after a
        // hit gets no sprint-jump boost, as in Minecraft (the hit tick ends before the jump tick).
        // The start distances put the hit in each of our three ticks of a Minecraft tick.
        VoxelWorld world = FlatWorld();
        world.Fill(new Vector3I(0, 4, 30), new Vector3I(63, 6, 30), Block.Stone); // wall at z = 30..31
        foreach (double offset in new[] { 0.0, 0.07, 0.13, 0.21, 0.29, 0.34 })
        {
            var (_, ours) = Pair(world, new Vec3d(32.5, FloorY, 34.5 + offset));
            int steps = 0;
            while (!ours.CollidedHorizontally && steps++ < 600)
            {
                ours.Step(Sprint(30));
            }
            string what = $"start {offset:F2} further";
            Check(ours.CollidedHorizontally && ours.Sprinting, $"{what}: still sprinting on the tick that hits the wall");
            ours.Step(new MovementInput(1, 0, true, true, false, 30));
            Check(!ours.Sprinting, $"{what}: the jump stops sprint before it pushes");
            Check(ours.McMotion.Y > 0, $"{what}: jumped");
        }
    }

    /// <summary>A short wall (x = 20..32, z = 30..31) and a one-block-high wall (x = 40..64, z = 10..11).</summary>
    private static VoxelWorld WallEdgesWorld()
    {
        VoxelWorld world = FlatWorld();
        world.Fill(new Vector3I(20, 4, 30), new Vector3I(31, 6, 30), Block.Stone);
        world.Fill(new Vector3I(40, 4, 10), new Vector3I(63, 4, 10), Block.Stone);
        return world;
    }

    public void TestBriefWallTouchesStopSprintLikeMinecraft()
    {
        // Clipping a wall's corner or its top edge for a moment: our three short moves meet it a
        // little differently from Minecraft's one long move, so positions can differ by a few
        // hundredths of a block, but sprint must stop and restart on the same ticks. That holds
        // because the wall check uses our last move of each Minecraft tick, which ends where
        // Minecraft's move ends; an earlier one can touch a corner that Minecraft's move clears.
        VoxelWorld world = WallEdgesWorld();
        foreach (var (name, start, input) in new (string, Vec3d, MovementInput)[]
        {
            ("cutting the corner at the end of a wall", new Vec3d(22.5, FloorY, 32.5), Sprint(60)),
            ("sprint-jumping over a one-block wall", new Vec3d(52.5, FloorY, 13.5), new MovementInput(1, 0, true, true, false, 20)),
        })
        {
            foreach (double offset in new[] { 0.0, 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.35 })
            {
                var (mc, ours) = Pair(world, start + new Vec3d(offset, 0, offset));
                string what = $"{name} (start {offset:F2} further)";
                bool hit = false;
                for (int t = 0; t < 40; t++)
                {
                    mc.Tick(input);
                    for (int s = 0; s < N; s++)
                    {
                        ours.Step(input);
                        hit |= ours.CollidedHorizontally;
                    }
                    CheckEqual(ours.Sprinting, mc.Sprinting, $"{what}: sprinting at MC tick {t}");
                }
                Check(hit, $"{what}: touches the wall");
            }
        }
    }

    public void TestNoTunnellingThroughThinWalls()
    {
        VoxelWorld world = FlatWorld();
        world.Fill(new Vector3I(0, 4, 30), new Vector3I(63, 6, 30), Block.Stone);
        var ours = new PlayerMotor(world, new Vec3d(32.5, FloorY, 32.5));
        ours.PushHorizontal(new Vec3d(0, 0, -50)); // absurdly fast
        for (int s = 0; s < 30; s++)
        {
            ours.Step(Idle());
        }
        Check(ours.Position.Z >= 31.3 - 1e-9, $"still on the near side of the wall (z = {ours.Position.Z:F3})");
    }

    public void TestCanJumpOntoOneBlockButNotTwo()
    {
        VoxelWorld world = FlatWorld();
        world.Fill(new Vector3I(0, 4, 28), new Vector3I(31, 4, 29), Block.Stone); // 1 high, west half
        world.Fill(new Vector3I(32, 4, 28), new Vector3I(63, 5, 29), Block.Stone); // 2 high, east half
        foreach (var (x, name, shouldClimb) in new[] { (20.5, "one block", true), (44.5, "two blocks", false) })
        {
            var ours = new PlayerMotor(world, new Vec3d(x, FloorY, 31.5));
            for (int s = 0; s < 120; s++)
            {
                ours.Step(new MovementInput(1, 0, true, false, false, 0));
            }
            bool climbed = ours.Position.Z < 30 && ours.Position.Y > FloorY + 0.5;
            Check(climbed == shouldClimb, $"{name}: climbed={climbed}, expected {shouldClimb}");
        }
    }

    public void TestSneakingStopsAtLedges()
    {
        VoxelWorld world = FlatWorld();
        world.Fill(new Vector3I(20, 4, 20), new Vector3I(24, 7, 24), Block.Stone); // a 5x5 pillar, top at y = 8
        var top = new Vec3d(22.5, 8, 22.5);
        var (mc, ours) = Pair(world, top);
        var sneakToEdge = new MovementInput(1, 0, false, false, true, 0);
        for (int t = 0; t < 60; t++)
        {
            StepMcTick(mc, ours, sneakToEdge);
        }
        Check(ours.OnGround && Math.Abs(ours.Position.Y - 8) < 1e-9, "still standing on the pillar");
        CheckNear(ours.Position.Z, mc.Position.Z, 0.05, "stops where Minecraft stops");
        Check(ours.Position.Z < 20 && ours.Position.Z > 19.6, $"hanging over the edge like Minecraft (z = {ours.Position.Z:F3})");

        var walker = new PlayerMotor(world, top);
        for (int s = 0; s < 120; s++)
        {
            walker.Step(Walk());
        }
        Check(walker.Position.Y < 8, "without sneaking, walks off the edge");
    }

    public void TestDoubleTapWSprints()
    {
        foreach (var (gapTicks, expectSprint) in new[] { (3, true), (9, false) })
        {
            var (mc, ours) = Pair(FlatWorld());
            var sequence = new System.Collections.Generic.List<MovementInput> { Walk() };
            for (int i = 0; i < gapTicks; i++)
            {
                sequence.Add(Idle());
            }
            for (int i = 0; i < 5; i++)
            {
                sequence.Add(Walk());
            }
            CheckMatchesEveryTick(mc, ours, sequence.Count, t => sequence[t], $"double tap with gap {gapTicks}");
            CheckEqual(ours.Sprinting, expectSprint, $"sprinting after a {gapTicks}-tick gap");
            CheckEqual(ours.Sprinting, mc.Sprinting, $"same as Minecraft after a {gapTicks}-tick gap");
        }
    }

    public void TestSneakingAndBackingOffStopSprinting()
    {
        var (_, ours) = Pair(FlatWorld());
        for (int s = 0; s < 30; s++)
        {
            ours.Step(Sprint());
        }
        Check(ours.Sprinting, "sprinting");
        ours.Step(new MovementInput(1, 0, false, true, true, 0));
        Check(!ours.Sprinting, "sneak stops sprint");
        for (int s = 0; s < 30; s++)
        {
            ours.Step(Sprint());
        }
        ours.Step(new MovementInput(0, 1, false, true, false, 0));
        Check(!ours.Sprinting, "letting go of W stops sprint");
    }

    public void TestHoldingJumpBunnyHopsLikeMinecraft()
    {
        var (mc, ours) = Pair(FlatWorld());
        int mcJumps = 0;
        int ourJumps = 0;
        bool mcWasGround = true;
        bool ourWasGround = true;
        var input = new MovementInput(1, 0, true, true, false, 0);
        for (int t = 0; t < 120; t++)
        {
            mc.Tick(input);
            if (mcWasGround && !mc.OnGround)
            {
                mcJumps++;
            }
            mcWasGround = mc.OnGround;
            for (int s = 0; s < N; s++)
            {
                ours.Step(input);
                if (ourWasGround && !ours.OnGround)
                {
                    ourJumps++;
                }
                ourWasGround = ours.OnGround;
            }
        }
        Check(Math.Abs(ourJumps - mcJumps) <= 1, $"jumps in 6 s: ours {ourJumps}, Minecraft {mcJumps}");
    }

    public void TestCeilingStopsJump()
    {
        VoxelWorld world = FlatWorld();
        world.Fill(new Vector3I(28, 6, 28), new Vector3I(36, 6, 36), Block.Stone); // ceiling 2 blocks up
        var (mc, ours) = Pair(world);
        double highest = 0;
        for (int t = 0; t < 20; t++)
        {
            StepMcTick(mc, ours, new MovementInput(0, 0, t == 0, false, false, 0));
            highest = Math.Max(highest, ours.Position.Y + Tuning.PlayerHeight);
        }
        Check(highest <= 6 + 1e-9, $"head never enters the ceiling (top reached {highest:F4})");
        Check(ours.OnGround, "back on the floor");
    }

    public void TestFrictionRegimesKeepMinecraftTopSpeed()
    {
        foreach (FrictionRegime r in new[] { MotionModel.Ground, MotionModel.Air })
        {
            // Steady speed per subtick must be one third of Minecraft's per tick.
            double ours = r.StateGain / (1 - r.SubFriction) + r.Feedthrough;
            double minecraft = 1.0 / (N * (1 - r.McFriction));
            CheckNear(ours, minecraft, 1e-12, $"steady speed for friction {r.McFriction}");
        }
    }
}
