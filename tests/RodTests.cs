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
        bobber.Hook(7, CastTime);
        CheckEqual(bobber.HookedId, 7, "caught");
        CheckNear(Bobber.HookPoint(new Vec3d(1, 2, 3)).Y, 2 + 1.08, 1e-12, "hangs at the chest");
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

    public void TestRodCooldownBlocksSpam()
    {
        // 0.75 s: short, but longer than the half second of hit protection, so back-to-back
        // rods can't keep someone pinned away.
        double seconds = RodCooldown.Ticks / (double)Tuning.SimRate;
        CheckNear(seconds, 0.75, 1e-9, "cooldown length (seconds)");
        Check(Tuning.RodCooldownMcTicks > Tuning.HurtImmunityThresholdMcTicks, "longer than hit protection");

        var cooldown = new RodCooldown();
        Check(cooldown.Ready && cooldown.FractionLeft == 0, "ready at the start");
        Check(cooldown.TryThrow(), "first right-click throws");
        CheckNear(cooldown.FractionLeft, 1, 1e-12, "full cooldown right after a throw");
        int throws = 0;
        double lastFraction = cooldown.FractionLeft;
        bool shrinking = true;
        for (int t = 1; t < RodCooldown.Ticks; t++)
        {
            cooldown.Tick();
            shrinking &= cooldown.FractionLeft < lastFraction;
            lastFraction = cooldown.FractionLeft;
            // Spam-clicking every tick of the cooldown does nothing.
            throws += cooldown.TryThrow() ? 1 : 0;
        }
        CheckEqual(throws, 0, "right-clicks during the cooldown that threw");
        Check(shrinking, "the cooldown shown on the slot shrinks every tick");
        Check(!cooldown.Ready, "still cooling down one tick before the end");
        cooldown.Tick();
        Check(cooldown.Ready && cooldown.FractionLeft == 0, "ready after exactly the cooldown");
        Check(cooldown.TryThrow(), "throws again once ready");

        cooldown.Reset();
        Check(cooldown.Ready && cooldown.TryThrow(), "a respawn makes the rod ready at once");
    }

    public void TestSpammingThrowsAtMostOncePerCooldown()
    {
        // Right-clicking 20 times a second for 3 seconds throws once per 0.75 s: 4 times.
        var cooldown = new RodCooldown();
        int throws = 0;
        for (int t = 0; t < 3 * Tuning.SimRate; t++)
        {
            if (t % 3 == 0)
            {
                throws += cooldown.TryThrow() ? 1 : 0;
            }
            cooldown.Tick();
        }
        CheckEqual(throws, 4, "throws in 3 s of spam-clicking");
    }

    private const double Frame = 1.0 / Tuning.SimRate;

    /// <summary>
    /// The first host time the bobber is gone, asking every 1/60 s from <paramref name="from"/>
    /// to <paramref name="until"/> as the game does each frame; null if it's still there.
    /// </summary>
    private static double? FirstGone(Bobber bobber, double from, double until)
    {
        for (int frame = 0; from + frame * Frame <= until; frame++)
        {
            if (bobber.IsGoneAt(from + frame * Frame))
            {
                return from + frame * Frame;
            }
        }
        return null;
    }

    /// <summary>Whether <paramref name="gone"/> is the first frame at or after <paramref name="due"/>.</summary>
    private static bool FirstFrameFrom(double? gone, double due) => gone is double g && g >= due - 1e-9 && g < due + Frame;

    public void TestBobberAtRestDisappearsAfterTheGroundTime()
    {
        Bobber bobber = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        double? gone = FirstGone(bobber, CastTime, CastTime + 6);
        double rest = bobber.RestingSince ?? double.NaN;
        double landed = bobber.LandedAt ?? double.NaN;
        // It touches down, slides a little, then lies still: its time counts from then.
        Check(rest > landed && rest < landed + 0.3, $"at rest a moment after touching down ({rest - landed:0.00} s later)");
        CheckNear(bobber.PositionAt(CastTime + 6).Y, FloorY, 1e-9, "lying on the floor");
        Check(FirstFrameFrom(gone, rest + Tuning.BobberGroundSeconds),
            $"gone the ground time after coming to rest, not before (rest {rest - CastTime:0.00} s, gone {gone - CastTime:0.00} s after the throw)");
        Check(!bobber.IsGoneAt(rest + Tuning.BobberGroundSeconds - 1e-6), "still there just before");
        Check(bobber.IsGoneAt(CastTime + 60), "and stays gone");
    }

    public void TestBobberAtRestOnTopOfAWallDisappearsToo()
    {
        VoxelWorld world = FlatWorld();
        // A wall across the floor, 5 deep with its top at y = 10, the bobber lobbed onto it.
        world.Fill(new Vector3I(0, (int)FloorY, 40), new Vector3I(63, 9, 44), Block.Stone);
        Bobber bobber = Cast(world, new Vec3d(32.5, FloorY, 55.5), 0, 35);
        double? gone = FirstGone(bobber, CastTime, CastTime + 6);
        CheckNear(bobber.PositionAt(CastTime + 6).Y, 10, 1e-9, "lying on top of the wall");
        Check(bobber.RestingSince is double rest && FirstFrameFrom(gone, rest + Tuning.BobberGroundSeconds),
            "gone the ground time after coming to rest there");
    }

    public void TestBobberStillMovingDoesNotDisappear()
    {
        // Nothing to land on: it falls for as long as you like (the game removes it below the world).
        Bobber falling = Cast(new VoxelWorld(new Vector3I(64, 40, 64)), new Vec3d(32.5, 30, 32.5), 0, 30);
        bool everResting = false;
        for (int frame = 0; frame <= 10 * Tuning.SimRate; frame++)
        {
            Check(!falling.IsGoneAt(CastTime + frame * Frame), "a bobber in the air doesn't disappear");
            everResting |= falling.RestingSince is not null;
        }
        Check(!everResting, "and never counts as at rest");

        // Thrown up into a tall wall it touches the wall, falls down along it, and lies at its
        // foot; thrown into a ceiling it stops for a moment against it, then falls to the floor.
        // Either way its time counts from lying on the floor, not from touching the wall.
        VoxelWorld wall = FlatWorld();
        wall.Fill(new Vector3I(0, (int)FloorY, 20), new Vector3I(63, 30, 20), Block.Stone);
        VoxelWorld ceiling = FlatWorld();
        ceiling.Fill(new Vector3I(0, 9, 0), new Vector3I(63, 9, 40), Block.Stone);
        foreach (var (name, bobber) in new[]
        {
            ("wall", Cast(wall, new Vec3d(32.5, FloorY, 27.5), 0, 20)),
            ("ceiling", Cast(ceiling, new Vec3d(32.5, FloorY, 45.5), 0, 40)),
        })
        {
            double? onFloor = null;
            double? gone = null;
            for (int frame = 0; gone is null && frame <= 6 * Tuning.SimRate; frame++)
            {
                double t = CastTime + frame * Frame;
                gone = bobber.IsGoneAt(t) ? t : null;
                onFloor ??= bobber.PositionAt(t).Y <= FloorY + 1e-9 ? t : (double?)null;
            }
            double touched = bobber.LandedAt ?? double.NaN;
            double floor = onFloor ?? double.NaN;
            Check(floor > touched + 0.5, $"{name}: falls a while after touching it ({floor - touched:0.00} s)");
            Check(bobber.RestingSince is double rest && rest >= floor - 1.0 / Tuning.McTickRate
                && FirstFrameFrom(gone, rest + Tuning.BobberGroundSeconds),
                $"{name}: gone the ground time after lying on the floor (touched {touched - CastTime:0.00} s, floor {floor - CastTime:0.00} s, gone {gone - CastTime:0.00} s after the throw)");
        }
    }

    public void TestHookedBobberDisappearsAfterTheHookTime()
    {
        Bobber bobber = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        double caught = At(3);
        bobber.Hook(7, caught);
        Check(FirstFrameFrom(FirstGone(bobber, CastTime, CastTime + 6), caught + Tuning.BobberHookSeconds),
            "hangs on them for the hook time, then it's gone");

        // Lying on the floor when someone walks into it: it hangs on them the hook time from
        // then, however long it lay there before.
        Bobber lying = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        lying.PositionAt(At(30));
        double rest = lying.RestingSince ?? double.NaN;
        double walkedInto = rest + 1.5;
        lying.Hook(7, walkedInto);
        Check(!lying.IsGoneAt(rest + Tuning.BobberGroundSeconds), "the time it lay there no longer counts");
        Check(FirstFrameFrom(FirstGone(lying, walkedInto, walkedInto + 6), walkedInto + Tuning.BobberHookSeconds),
            "gone the hook time after it caught them");
    }

    public void TestDroppedBobberDisappearsAfterTheGroundTime()
    {
        Bobber bobber = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        double caught = At(3);
        bobber.Hook(7, caught);
        // The player it hangs on dies a second later: it drops from their chest.
        double died = caught + 1;
        bobber.Release(died, Bobber.HookPoint(new Vec3d(32.5, FloorY, 50.5)));
        Check(bobber.HookedAt is null, "no longer hanging on anyone");
        double? gone = FirstGone(bobber, died, died + 6);
        double rest = bobber.RestingSince ?? double.NaN;
        // Its first moment after letting go it doesn't move yet, but it falls right after.
        Check(rest > died + 0.3, $"at rest once it has fallen to the floor ({rest - died:0.00} s after the drop)");
        Check(gone > caught + Tuning.BobberHookSeconds, "the hook's time no longer counts");
        Check(FirstFrameFrom(gone, rest + Tuning.BobberGroundSeconds), "gone the ground time after landing, not before");
    }

    public void TestGoneBobberCatchesNoOne()
    {
        // While it flies it catches as always: someone 5 blocks ahead, as it reaches them.
        Bobber flying = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        var ahead = new List<RodTarget> { new(5, new Vec3d(32.5, FloorY, 50.5)) };
        (int Id, double Time)? caught = null;
        for (int frame = 0; caught is null && frame < 2 * Tuning.SimRate; frame++)
        {
            caught = flying.Catch(CastTime + frame * Frame, CastTime + (frame + 1) * Frame, ahead);
        }
        // Their hitbox counts from 4.4 blocks out, which it passes in its 4th tick (4.15 to 5.32 blocks).
        CheckEqual(caught?.Id, 5, "catches the player ahead");
        CheckNear(caught?.Time ?? -1, At(3) + (4.4 - 4.1496) / (5.317632 - 4.1496) / Tuning.McTickRate, 1e-6,
            "when it reaches them");

        // Lying on the floor, it catches someone standing on it until it's gone, and then no one.
        Bobber lying = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        var onIt = new List<RodTarget> { new(6, lying.PositionAt(At(30))) };
        double gone = lying.GoneAt ?? double.NaN;
        CheckEqual(lying.Catch(gone - 1, gone - 1 + Frame, onIt)?.Id, 6, "caught while it's there");
        Check(lying.Catch(gone - Frame / 2, gone + Frame / 2, onIt) is { Time: var time } && time < gone,
            "in the frame it goes, only before it goes");
        int catches = 0;
        for (int frame = 0; frame < 3 * Tuning.SimRate; frame++)
        {
            catches += lying.Catch(gone + frame * Frame, gone + (frame + 1) * Frame, onIt) is null ? 0 : 1;
        }
        CheckEqual(catches, 0, "catches after it's gone");
        Check(lying.IsGoneAt(gone), "gone by then");
    }
}
