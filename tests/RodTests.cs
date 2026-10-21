using System;
using System.Collections.Generic;
using Godot;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

/// <summary>
/// The fishing rod: the bobber's cast and flight against <see cref="McBobber"/> (1.8.9's), landing
/// and walls, which players it catches, and how rod hits count for hit protection.
/// </summary>
public class RodTests : TestCase
{
    private const double FloorY = 4;
    private const double CastTime = 10.0;

    /// <summary>A 64x64 stone floor with its top at y = 4, and room above.</summary>
    private static VoxelWorld FlatWorld()
    {
        var world = new VoxelWorld(new Vector3I(64, 40, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, (int)FloorY - 1, 63), Block.Stone);
        return world;
    }

    private static double Distance(Vec3d a, Vec3d b)
    {
        Vec3d d = a - b;
        return Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
    }

    private static double At(int mcTicks) => CastTime + mcTicks / (double)Tuning.McTickRate;

    /// <summary>A level cast, without spread, by a player standing at <paramref name="feet"/>.</summary>
    private static Bobber Cast(VoxelWorld world, Vec3d feet, double yaw, double pitch)
    {
        Vec3d eye = feet + new Vec3d(0, Tuning.EyeHeight, 0);
        return new Bobber(world, 1, CastTime, Bobber.CastOrigin(eye, yaw), Bobber.CastVelocity(yaw, pitch, Vec3d.Zero));
    }

    public void TestCastAndFlightMatchMinecraftInOpenAir()
    {
        VoxelWorld world = FlatWorld();
        var feet = new Vec3d(32.5, 26, 32.5);
        Vec3d eye = feet + new Vec3d(0, Tuning.EyeHeight, 0);
        foreach (var (yaw, pitch) in new[] { (0.0, 0.0), (37.0, 25.0), (-120.0, -10.0), (170.0, -60.0) })
        {
            // Minecraft's angles: yaw 0 faces +Z and turns the other way; positive pitch looks down.
            var mc = new McBobber(feet, Tuning.EyeHeight, 180 - yaw, -pitch);
            Vec3d origin = Bobber.CastOrigin(eye, yaw);
            // Minecraft's bobber starts at the right hand, ours at the left.
            Vec3d mirrored = origin - Vec3d.Left(yaw) * (2 * Tuning.RodCastSideOffset);
            CheckNear(Distance(mirrored, mc.Position), 0, 1e-9, $"start point (yaw {yaw}, pitch {pitch})");
            Vec3d velocity = Bobber.CastVelocity(yaw, pitch, Vec3d.Zero);
            CheckNear(Distance(velocity, mc.Motion), 0, 1e-9, $"launch motion (yaw {yaw}, pitch {pitch})");

            var bobber = new Bobber(world, 1, CastTime, mc.Position, velocity);
            double worst = 0;
            for (int tick = 0; tick <= 15; tick++)
            {
                worst = Math.Max(worst, Distance(bobber.PositionAt(At(tick)), mc.Position));
                mc.Tick();
            }
            CheckNear(worst, 0, 1e-9, $"flight (yaw {yaw}, pitch {pitch})");
        }
    }

    public void TestLevelThrowLandsAboutTenBlocksAway()
    {
        var feet = new Vec3d(32.5, FloorY, 55.5);
        Bobber bobber = Cast(FlatWorld(), feet, 0, 0);
        double startZ = bobber.PositionAt(At(0)).Z;
        // 1.8.9's flight: 1.5, 2.88, 4.15, 5.32 and 6.39 blocks out after 1 to 5 ticks.
        double[] expected = { 1.5, 2.88, 4.1496, 5.317632, 6.39222144 };
        for (int tick = 1; tick <= expected.Length; tick++)
        {
            CheckNear(startZ - bobber.PositionAt(At(tick)).Z, expected[tick - 1], 1e-9, $"distance after {tick} ticks");
        }
        Vec3d rest = bobber.PositionAt(At(40));
        Check(bobber.Landed, "lying on the floor");
        CheckNear(rest.Y, FloorY, 1e-9, "rests on top of the floor");
        double distance = startZ - rest.Z;
        Check(distance > 10.4 && distance < 11.3, $"lands about 10.6 blocks out, like 1.8.9 (got {distance:0.00})");
        CheckNear(Distance(bobber.PositionAt(At(80)), rest), 0, 1e-6, "stays put");
    }

    public void TestBetweenTicksItMovesInAStraightLine()
    {
        Bobber bobber = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 20, 30);
        Vec3d a = bobber.PositionAt(At(2));
        Vec3d third = bobber.PositionAt(CastTime + 2.25 / Tuning.McTickRate);
        Vec3d b = bobber.PositionAt(At(3));
        CheckNear(Distance(third, a + (b - a) * 0.25), 0, 1e-9, "a quarter of the way along the tick's move");
        CheckNear(Distance(bobber.PositionAt(At(2)), b), 0, 1e-9, "a time already passed shows the start of the latest tick");
    }

    public void TestBobberStopsAtWalls()
    {
        VoxelWorld world = FlatWorld();
        // A wall across the floor whose near face is at z = 21.
        world.Fill(new Vector3I(0, (int)FloorY, 20), new Vector3I(63, 12, 20), Block.Stone);
        Bobber bobber = Cast(world, new Vec3d(32.5, FloorY, 27.5), 0, 0);
        double nearest = double.MaxValue;
        for (int subtick = 0; subtick <= 60 * 3; subtick++)
        {
            Vec3d p = bobber.PositionAt(CastTime + subtick / (double)Tuning.SimRate);
            nearest = Math.Min(nearest, Bobber.BoxAt(p).MinZ);
        }
        Check(nearest >= 21 - 1e-9, $"never inside the wall (closest edge at z = {nearest:0.000})");
        Vec3d rest = bobber.PositionAt(At(60));
        CheckNear(rest.Y, FloorY, 1e-9, "slides down the wall to the floor");
        CheckNear(Bobber.BoxAt(rest).MinZ, 21, 0.05, "and lies against it");
    }

    public void TestCatchesTheFirstPlayerInItsPath()
    {
        Vec3d from = new(0, 1, 0);
        Vec3d to = new(0, 1, -10);
        var far = new RodTarget(1, new Vec3d(0, 0, -5));
        var near = new RodTarget(2, new Vec3d(0, 0, -3));
        // Hitboxes count 0.3 + 0.3 out from the feet (half the width plus the bobber's border).
        var hit = Bobber.FirstHit(from, to, new List<RodTarget> { far });
        Check(hit is { Id: 1 }, "catches the player ahead");
        CheckNear(hit?.Fraction ?? -1, 0.44, 1e-9, "where it touches the enlarged hitbox");
        CheckEqual(Bobber.FirstHit(from, to, new List<RodTarget> { far, near })?.Id, 2, "the nearer of two players");
        Check(Bobber.FirstHit(from, to, new List<RodTarget> { new(3, new Vec3d(0.61, 0, -5)) }) is null, "misses just beside");
        Check(Bobber.FirstHit(from, to, new List<RodTarget> { new(3, new Vec3d(0.59, 0, -5)) }) is not null, "catches at the very edge");
        Check(Bobber.FirstHit(new Vec3d(0, 2.11, 0), new Vec3d(0, 2.11, -10), new List<RodTarget> { far }) is null, "passes over the head");
        Check(Bobber.FirstHit(new Vec3d(0, 2.09, 0), new Vec3d(0, 2.09, -10), new List<RodTarget> { far }) is not null, "clips the top of the head");
        CheckEqual(Bobber.FirstHit(new Vec3d(0, 1, -5), new Vec3d(0, 1, -5), new List<RodTarget> { far })?.Fraction, 0.0, "already inside");
    }

    public void TestHookAndRelease()
    {
        Bobber bobber = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        bobber.Hook(7);
        CheckEqual(bobber.HookedId, 7, "caught");
        CheckNear(Bobber.HookPoint(new Vec3d(1, 2, 3)).Y, 2 + 1.44, 1e-12, "hangs at 0.8 of the player's height");
        var hangPoint = new Vec3d(20.5, 12, 20.5);
        bobber.Release(20.0, hangPoint);
        Check(bobber.HookedId is null, "released");
        Vec3d later = bobber.PositionAt(21.0);
        Check(later.Y < hangPoint.Y, "falls from where it hung");
        CheckNear(later.X, hangPoint.X, 1e-12, "straight down");
    }

    public void TestRodHitsCountForHitProtection()
    {
        var protection = new HitProtection();
        CheckEqual(protection.Hit(Tuning.RodDamage, 100), (HitResult.Hurt, 0.0), "a rod hit counts as a full hit with no damage");
        CheckEqual(protection.Hit(Tuning.RodDamage, 105).Result, HitResult.Ignored, "another rod hit during protection does nothing");
        CheckEqual(protection.Hit(Tuning.SwordDamage, 105), (HitResult.Stronger, Tuning.SwordDamage),
            "a sword hit right after a rod does full damage but no knockback");
        CheckEqual(protection.Hit(Tuning.SwordDamage, 109).Result, HitResult.Ignored, "and doesn't restart protection");
        CheckEqual(protection.Hit(Tuning.RodDamage, 110).Result, HitResult.Hurt, "half a second after the rod hit, a rod hit lands again");

        var vitality = new Vitality();
        CheckEqual(vitality.TakeHit(Tuning.RodDamage, Tuning.KitArmorPoints, 0), HitResult.Hurt, "rod hit on a player");
        CheckNear(vitality.Health, Tuning.MaxHealth, 1e-12, "no damage");
        Check(vitality.HurtFlash > 0, "but the red flash plays");
    }

    public void TestCastSpreadIsSmall()
    {
        var random = new Random(42);
        Vec3d look = HitScan.LookVector(30, 10);
        double worstAngle = 0;
        for (int i = 0; i < 500; i++)
        {
            Vec3d v = Bobber.CastVelocity(30, 10, random);
            double speed = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            double cos = (v.X * look.X + v.Y * look.Y + v.Z * look.Z) / speed;
            worstAngle = Math.Max(worstAngle, Math.Acos(Math.Min(cos, 1)) * 180 / Math.PI);
            CheckNear(speed, Tuning.RodCastSpeed, 0.05, "launch speed");
        }
        Check(worstAngle < 2.5, $"at most a couple of degrees off the crosshair (worst {worstAngle:0.00})");
        Check(worstAngle > 0.1, "but not perfectly straight");
    }
}
