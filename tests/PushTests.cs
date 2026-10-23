using System;
using Godot;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

/// <summary>
/// The player pushing twist (a match rule; 1.8.9 has none, players walk through each other).
/// Each game pushes its own player away from anyone overlapping it, on Minecraft's tick rhythm.
/// </summary>
public class PushTests : TestCase
{
    private const int N = 3;
    private const double FloorY = 4;
    private const double Full = 1.0;

    private static VoxelWorld FlatWorld()
    {
        var world = new VoxelWorld(new Vector3I(64, 24, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, (int)FloorY - 1, 63), Block.Stone);
        return world;
    }

    private static Vec3d At(double x, double z = 32.5, double y = FloorY) => new(x, y, z);

    public void TestOffIsMinecraft()
    {
        CheckEqual(Pushing.Away(At(32.5), At(32.6), 0, true), Vec3d.Zero, "no push when the twist is off");
        CheckEqual(Tuning.DefaultPushing, 0.0, "off by default");
        var (minGap, _, _) = RunInto(0, sprint: true, otherSprints: false, jump: false);
        Check(minGap < -1, "with the twist off you run straight through a standing player, like 1.8.9");
    }

    public void TestPushPointsAwayAndGrowsWithOverlap()
    {
        Vec3d push = Pushing.Away(At(32.5), At(32.8), Full, true);
        Check(push.X < 0 && push.Z == 0 && push.Y == 0, $"pushed away from the other player: {push}");
        Vec3d diagonal = Pushing.Away(At(32.5, 32.5), At(32.7, 32.7), Full, true);
        CheckNear(diagonal.X, diagonal.Z, 1e-12, "straight away on a diagonal");
        Check(diagonal.X < 0, "away on a diagonal");
        double shallow = Pushing.Away(At(32.5), At(33.05), Full, true).HorizontalLength;
        double deep = Pushing.Away(At(32.5), At(32.6), Full, true).HorizontalLength;
        Check(deep > shallow * 4, $"deeper overlap pushes harder: {deep:0.000} vs {shallow:0.000}");
        CheckNear(Pushing.Away(At(32.5), At(32.5 + 0.3), Full, true).HorizontalLength, Tuning.PushImpulse / 2, 1e-12,
            "half overlap, half the push");
        CheckNear(Pushing.Away(At(32.5), At(32.5 + 0.3), 2.0, true).HorizontalLength, Tuning.PushImpulse, 1e-12,
            "200% is twice as hard");
    }

    public void TestOnlyOverlappingPlayersPush()
    {
        CheckEqual(Pushing.Away(At(32.5), At(33.1), Full, true), Vec3d.Zero, "boxes just touching");
        Check(Pushing.Away(At(32.5), At(33.09), Full, true) != Vec3d.Zero, "boxes overlapping a little");
        CheckEqual(Pushing.Away(At(32.5), At(32.5, 32.5, FloorY + 1.8), Full, true), Vec3d.Zero, "standing on someone's head");
        Check(Pushing.Away(At(32.5), At(32.6, 32.5, FloorY + 1.7), Full, true) != Vec3d.Zero, "boxes overlapping in height");
        CheckEqual(Pushing.Away(At(32.5), At(32.505), Full, true), Vec3d.Zero, "exactly on top of each other: no direction");
    }

    public void TestAirPushIsGentler()
    {
        double ground = Pushing.Away(At(32.5), At(32.7), Full, true).HorizontalLength;
        double air = Pushing.Away(At(32.5), At(32.7), Full, false).HorizontalLength;
        CheckNear(air, ground * Tuning.PushAirFactor, 1e-12, "halved in the air");
    }

    /// <summary>
    /// Our 60 ticks/s motor adds a push exactly as a 20 ticks/s Minecraft player adding it to its
    /// motion after each move would: positions match at every Minecraft tick, on the ground and
    /// sprint-jumping.
    /// </summary>
    public void TestPushesKeepMinecraftsRhythm()
    {
        foreach (bool jumping in new[] { false, true })
        {
            VoxelWorld world = FlatWorld();
            var mc = new McReference(world, At(32.5));
            var ours = new PlayerMotor(world, At(32.5));
            var still = new MovementInput(0, 0, false, true, false, 0);
            for (int t = 0; t < 5; t++)
            {
                mc.Tick(still);
                for (int s = 0; s < N; s++)
                {
                    ours.Step(still);
                }
            }
            double worst = 0;
            for (int t = 0; t < 60; t++)
            {
                var keys = new MovementInput(1, t % 20 < 10 ? 1 : -1, jumping, true, false, 20);
                Vec3d push = t % 3 == 0 ? new Vec3d(0.12, 0, -0.07) : t % 5 == 0 ? new Vec3d(-0.2, 0, 0.03) : Vec3d.Zero;
                // Minecraft adds the push after a tick's move; the next tick starts with it.
                mc.Motion += push;
                mc.Tick(keys);
                ours.Push = push;
                for (int s = 0; s < N; s++)
                {
                    ours.Step(keys);
                }
                worst = Math.Max(worst, (ours.Position - mc.Position).Length);
            }
            Check(worst < 1e-9, $"{(jumping ? "sprint-jumping" : "on the ground")}: off by {worst:E2} at worst");
        }
    }

    public void TestKnockbackReplacesAPush()
    {
        VoxelWorld world = FlatWorld();
        var mc = new McReference(world, At(32.5));
        var ours = new PlayerMotor(world, At(32.5));
        var still = new MovementInput(0, 0, false, true, false, 0);
        for (int t = 0; t < 5; t++)
        {
            mc.Tick(still);
            for (int s = 0; s < N; s++)
            {
                ours.Step(still);
            }
        }
        var knockback = new Vec3d(0.4, 0.36, -0.1);
        // Minecraft: the push lands on the motion after the move, then the velocity packet
        // replaces the motion. Ours: the push for the next tick is dropped after knockback.
        mc.Motion += new Vec3d(0.1, 0, 0.1);
        mc.ReceiveVelocity(knockback);
        ours.SetMcMotion(knockback);
        ours.Push = new Vec3d(0.1, 0, 0.1);
        double worst = 0;
        for (int t = 0; t < 20; t++)
        {
            mc.Tick(still);
            for (int s = 0; s < N; s++)
            {
                ours.Step(still);
            }
            worst = Math.Max(worst, (ours.Position - mc.Position).Length);
        }
        Check(worst < 1e-9, $"knockback stays whole: off by {worst:E2}");
    }

    /// <summary>
    /// Player A runs at player B (yaw -90 faces +X) on flat ground, each game pushing its own
    /// player, for four seconds. Returns the smallest gap between them along X (negative once A
    /// is past B), how far B was moved, and the gap at the end.
    /// </summary>
    private static (double MinGap, double OtherMoved, double FinalGap) RunInto(
        double strength, bool sprint, bool otherSprints, bool jump)
    {
        VoxelWorld world = FlatWorld();
        var a = new PlayerMotor(world, At(30.5));
        var b = new PlayerMotor(world, At(33.5));
        double minGap = double.MaxValue;
        double moved = 0;
        for (int s = 0; s < 240; s++)
        {
            var keysA = new MovementInput(1, 0, jump, sprint, false, -90);
            var keysB = new MovementInput(otherSprints ? 1 : 0, 0, false, true, false, 90);
            a.Push = Pushing.Away(a.Position, b.Position, strength, a.OnGround);
            b.Push = Pushing.Away(b.Position, a.Position, strength, b.OnGround);
            a.Step(keysA);
            b.Step(keysB);
            minGap = Math.Min(minGap, b.Position.X - a.Position.X);
            moved = Math.Max(moved, Math.Abs(b.Position.X - 33.5));
        }
        return (minGap, moved, b.Position.X - a.Position.X);
    }

    public void TestNobodyRunsThroughAnyoneAtFullStrength()
    {
        foreach (var (name, sprint, otherSprints, jump) in new[]
        {
            ("walking into a standing player", false, false, false),
            ("sprinting into a standing player", true, false, false),
            ("both sprinting head-on", true, true, false),
            ("sprint-jumping into a standing player", true, false, true),
        })
        {
            var (minGap, moved, finalGap) = RunInto(Full, sprint, otherSprints, jump);
            Check(minGap > 0, $"{name}: never passes through (closest {minGap:0.00})");
            Check(finalGap > 0.3, $"{name}: ends up mostly apart ({finalGap:0.00})");
            if (!otherSprints)
            {
                Check(moved > 3, $"{name}: shoves the standing player along ({moved:0.0} blocks)");
            }
        }
        var (gentleGap, _, _) = RunInto(0.2, sprint: true, otherSprints: false, jump: false);
        Check(gentleGap < -1, "at 20% a sprinting player still gets through, slowed");
    }

    public void TestOverlappingPlayersSeparateSmoothly()
    {
        VoxelWorld world = FlatWorld();
        var a = new PlayerMotor(world, At(32.45));
        var b = new PlayerMotor(world, At(32.55));
        var still = new MovementInput(0, 0, false, true, false, 0);
        double fastest = 0;
        for (int s = 0; s < 30; s++)
        {
            a.Push = Pushing.Away(a.Position, b.Position, Full, a.OnGround);
            b.Push = Pushing.Away(b.Position, a.Position, Full, b.OnGround);
            a.Step(still);
            b.Step(still);
            fastest = Math.Max(fastest, a.HorizontalSpeed);
        }
        Check(b.Position.X - a.Position.X > 0.5, $"apart within half a second ({b.Position.X - a.Position.X:0.00})");
        Check(fastest < 8, $"a shove, not a launch ({fastest:0.0} blocks/s; sprinting is 5.6)");
    }
}
