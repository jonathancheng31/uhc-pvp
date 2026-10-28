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
        CheckNear(Distance(bobber.PositionAt(At(2)), a), 0, 1e-9, "a time already passed gives where it was then");
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
        // 0.75 s by default: short, but longer than the half second of hit protection, so
        // back-to-back rods can't keep someone pinned away.
        double seconds = MatchRules.Default.RodCooldown;
        CheckNear(seconds, 0.75, 1e-9, "cooldown length by default (seconds)");
        CheckEqual(RodCooldown.TicksFor(seconds), 45, "45 of our ticks");
        Check(seconds > Tuning.HurtImmunityThresholdMcTicks / (double)Tuning.McTickRate, "longer than hit protection");

        var cooldown = new RodCooldown();
        Check(cooldown.Ready && cooldown.FractionLeft == 0, "ready at the start");
        Check(cooldown.TryThrow(seconds), "first right-click throws");
        CheckNear(cooldown.FractionLeft, 1, 1e-12, "full cooldown right after a throw");
        int throws = 0;
        double lastFraction = cooldown.FractionLeft;
        bool shrinking = true;
        for (int t = 1; t < RodCooldown.TicksFor(seconds); t++)
        {
            cooldown.Tick();
            shrinking &= cooldown.FractionLeft < lastFraction;
            lastFraction = cooldown.FractionLeft;
            // Spam-clicking every tick of the cooldown does nothing.
            throws += cooldown.TryThrow(seconds) ? 1 : 0;
        }
        CheckEqual(throws, 0, "right-clicks during the cooldown that threw");
        Check(shrinking, "the cooldown bar shrinks every tick");
        Check(!cooldown.Ready, "still cooling down one tick before the end");
        cooldown.Tick();
        Check(cooldown.Ready && cooldown.FractionLeft == 0, "ready after exactly the cooldown");
        Check(cooldown.TryThrow(seconds), "throws again once ready");

        cooldown.Reset();
        Check(cooldown.Ready && cooldown.TryThrow(seconds), "a respawn makes the rod ready at once");
    }

    public void TestRodCooldownLastsTheMatchRule()
    {
        // The host can set it from off to 1.5 s, in steps of one Minecraft tick.
        foreach ((double seconds, int ticks) in new[] { (0.05, 3), (0.5, 30), (1.2, 72), (Tuning.MaxRodCooldown, 90) })
        {
            var cooldown = new RodCooldown();
            Check(cooldown.TryThrow(seconds), $"{seconds} s: the first right-click throws");
            int blocked = 0;
            while (!cooldown.Ready)
            {
                blocked++;
                CheckNear(cooldown.FractionLeft, (ticks - blocked + 1) / (double)ticks, 1e-12, $"{seconds} s: the bar after {blocked - 1} ticks");
                Check(!cooldown.TryThrow(seconds), $"{seconds} s: no throw while cooling down");
                cooldown.Tick();
            }
            CheckEqual(blocked, ticks, $"{seconds} s blocks for {ticks} of our ticks");
        }
    }

    public void TestRodCooldownOffThrowsEveryClick()
    {
        // Off, as in 1.8.9: every right-click throws (the old bobber vanishes and a new one flies).
        CheckEqual(MatchRules.Minecraft.RodCooldown, 0.0, "plain 1.8.9 has no rod cooldown");
        var cooldown = new RodCooldown();
        int throws = 0;
        for (int t = 0; t < Tuning.SimRate; t++)
        {
            throws += cooldown.TryThrow(0) ? 1 : 0;
            Check(cooldown.Ready && cooldown.FractionLeft == 0, "never cooling down, so no bar");
            cooldown.Tick();
        }
        CheckEqual(throws, Tuning.SimRate, "right-clicking every tick for a second throws every time");
    }

    public void TestRodCooldownKeepsItsLengthWhenTheRuleChanges()
    {
        // The host moves the slider mid-cooldown: the running one keeps its length (and its bar
        // stays right), and the new length counts from the next throw.
        var cooldown = new RodCooldown();
        cooldown.TryThrow(1.0);
        for (int t = 0; t < 30; t++)
        {
            cooldown.Tick();
            Check(!cooldown.TryThrow(0), "a shorter rule doesn't end a cooldown that's running");
        }
        CheckNear(cooldown.FractionLeft, 0.5, 1e-12, "halfway through 1 s, the bar is half full");
        for (int t = 0; t < 30; t++)
        {
            cooldown.Tick();
        }
        Check(cooldown.Ready, "ready after the 1 s it started with");
        Check(cooldown.TryThrow(0.25), "throws");
        CheckNear(cooldown.FractionLeft, 1, 1e-12, "a full bar for the new length");
        for (int t = 0; t < RodCooldown.TicksFor(0.25); t++)
        {
            cooldown.Tick();
        }
        Check(cooldown.Ready, "the next cooldown lasts the new 0.25 s");
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
                throws += cooldown.TryThrow(MatchRules.Default.RodCooldown) ? 1 : 0;
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

    public void TestBobberAtRestCatchesNoOne()
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

        // Once it lies still it's a leftover: someone standing on it, or walking onto it, isn't
        // caught (1.8.9's lying bobber practically never catches anyone either).
        Bobber lying = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        var onIt = new List<RodTarget> { new(6, lying.PositionAt(At(30))) };
        double rest = lying.RestingSince ?? double.NaN;
        double gone = lying.GoneAt ?? double.NaN;
        Check(lying.Catch(rest - Frame / 2, rest + Frame / 2, onIt) is not { } c || c.Time < rest,
            "in the frame it comes to rest, only before it does");
        int catches = 0;
        for (int frame = 0; rest + frame * Frame < gone + 1; frame++)
        {
            catches += lying.Catch(rest + frame * Frame, rest + (frame + 1) * Frame, onIt) is null ? 0 : 1;
        }
        CheckEqual(catches, 0, "catches once it lies still, until it's gone and after");
        Check(lying.IsGoneAt(gone), "gone by then");
    }

    public void TestAnEarlierTimeAfterALaterOneGivesWhereItWasThen()
    {
        // A host bot's bobber: its catches are checked now, and it's drawn the head start ago.
        Bobber bobber = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        Bobber inOrder = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
        double behind = Tuning.DefaultHeadStart;
        double worst = 0;
        for (int frame = 0; frame <= Tuning.SimRate; frame++)
        {
            double drawn = CastTime + frame * Frame;
            bobber.PositionAt(drawn + behind);
            worst = Math.Max(worst, Distance(bobber.PositionAt(drawn), inOrder.PositionAt(drawn)));
        }
        CheckNear(worst, 0, 1e-12, "drawn where it was then, not ahead");
        // Smooth: a frame later it has moved a frame's worth along its 2nd tick (a third of that
        // tick's 1.38 blocks, a little down too), not held still until the next tick.
        bobber.PositionAt(At(3));
        Vec3d a = bobber.PositionAt(At(1) + Frame / 2);
        Vec3d b = bobber.PositionAt(At(1) + 1.5 * Frame);
        CheckNear(Distance(a, b), 1.380491 / 3, 1e-5, "moves every frame");
    }

    public void TestTheTimeItVanishesDoesntDependOnTheOrderItsAskedIn()
    {
        // As above, one asked ahead then behind and one asked in order, from a throw that lands
        // and lies still, then one caught and dropped: the same answer every frame.
        foreach (bool hooked in new[] { false, true })
        {
            Bobber bobber = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
            Bobber inOrder = Cast(FlatWorld(), new Vec3d(32.5, FloorY, 55.5), 0, 0);
            int disagree = 0;
            for (int frame = 0; frame <= 6 * Tuning.SimRate; frame++)
            {
                double drawn = CastTime + frame * Frame;
                if (hooked && frame == 9)
                {
                    bobber.Hook(7, drawn);
                    inOrder.Hook(7, drawn);
                }
                if (hooked && frame == 60)
                {
                    bobber.Release(drawn, new Vec3d(32.5, FloorY + 1.08, 50.5));
                    inOrder.Release(drawn, new Vec3d(32.5, FloorY + 1.08, 50.5));
                }
                bobber.IsGoneAt(drawn + Tuning.DefaultHeadStart);
                disagree += bobber.IsGoneAt(drawn) == inOrder.IsGoneAt(drawn)
                    && Distance(bobber.PositionAt(drawn), inOrder.PositionAt(drawn)) < 1e-12 ? 0 : 1;
            }
            CheckEqual(disagree, 0, $"frames where asking ahead first changes the answer ({(hooked ? "caught and dropped" : "lying still")})");
            Check(inOrder.IsGoneAt(CastTime + 6), "and it does vanish");
        }
    }

    // --- Rod knockback (a twist) ------------------------------------------------------------

    private static readonly MovementInput Idle = new(0, 0, false, false, false, 0);
    /// <summary>Sprinting at the thrower (who stands toward +Z), so the victim's own motion is halved by a knockback.</summary>
    private static readonly MovementInput ChargeTheThrower = new(1, 0, false, true, false, 180);

    /// <summary>A player standing on the floor, settled as players are a few ticks into a match.</summary>
    private static PlayerMotor Settled(VoxelWorld world, Vec3d feet)
    {
        var motor = new PlayerMotor(world, feet);
        for (int s = 0; s < 5 * Tuning.SubticksPerMcTick; s++)
        {
            motor.Step(Idle);
        }
        return motor;
    }

    private static MatchRules RodKnockback(double strength) => MatchRules.Default with { RodKnockback = strength };

    /// <summary>Whether two motors are in exactly the same state (where they are, how they move, and the server's copy).</summary>
    private static bool Same(PlayerMotor a, PlayerMotor b) =>
        a.Position == b.Position && a.Velocity == b.Velocity && a.ServerMotion == b.ServerMotion
        && a.KnockbackFlight == b.KnockbackFlight && a.OnGround == b.OnGround;

    public void TestRodKnockbackScalesThePushTheLiftAndItsCap()
    {
        var random = new Random(1);
        // The thrower stands straight toward +Z, so the push is all along -Z.
        var thrower = new Vec3d(32.5, FloorY, 55.5);
        var victim = new Vec3d(32.5, FloorY, 50.5);
        Vec3d Rod(Vec3d copy, double strength) =>
            CombatRules.Knockback(copy, thrower, victim, 0, true, false, random, Tuning.McComboKnockback, strength);
        var standing = new Vec3d(0, -0.0784, 0);
        var moving = new Vec3d(0.2, -0.3, -0.12);
        var rising = new Vec3d(0, 0.3, 0);
        foreach (Vec3d copy in new[] { standing, moving, rising })
        {
            CheckEqual(Rod(copy, 1.0), CombatRules.Knockback(copy, thrower, victim, 0, true, false, random),
                $"100% is exactly an unsprinted sword hit's, today's rod knockback ({copy})");
            foreach (double strength in new[] { 0.5, 1.5, 2.0 })
            {
                Vec3d k = Rod(copy, strength);
                CheckNear(k.X, copy.X / 2, 1e-12, $"{strength:P0}: sideways the victim's motion is only halved ({copy})");
                CheckNear(k.Z, copy.Z / 2 - Tuning.KnockbackHorizontal * strength, 1e-12, $"{strength:P0}: the push away is scaled ({copy})");
            }
        }
        // The lift: halve the copy's, add 0.4, at most 0.4; all three 0.4s scaled.
        CheckNear(Rod(standing, 1.0).Y, 0.3608, 1e-12, "standing, 100%: 1.8's lift");
        CheckNear(Rod(standing, 0.5).Y, 0.1608, 1e-12, "standing, 50%: half the 0.4 added");
        CheckNear(Rod(standing, 1.5).Y, 0.5608, 1e-12, "standing, 150%: half again");
        CheckNear(Rod(standing, 2.0).Y, 0.7608, 1e-12, "standing, 200%: twice");
        CheckNear(Rod(rising, 1.0).Y, 0.4, 1e-12, "rising, 100%: capped at 0.4");
        CheckNear(Rod(rising, 0.5).Y, 0.2, 1e-12, "rising, 50%: the cap is halved too");
        CheckNear(Rod(rising, 1.5).Y, 0.6, 1e-12, "rising, 150%: the cap is 0.6");
        CheckNear(Rod(moving, 0.5).Y, 0.05, 1e-12, "falling, 50%: the halving stays as it is (-0.15 + 0.2)");
    }

    public void TestRodKnockbackAtFullIsTodaysExactly()
    {
        // A rod catch through the shared landing at 100% against the knockback rod catches got
        // before it was a twist, built the same way, on a victim charging the thrower.
        VoxelWorld world = FlatWorld();
        var thrower = new Vec3d(32.5, FloorY, 55.5);
        PlayerMotor twist = Settled(world, new Vec3d(32.5, FloorY, 48.5));
        PlayerMotor before = Settled(world, new Vec3d(32.5, FloorY, 48.5));
        for (int s = 0; s < 20; s++)
        {
            twist.Step(ChargeTheThrower);
            before.Step(ChargeTheThrower);
        }
        Vec3d at = twist.Position;
        CombatRules.ApplyKnockback(twist, HitResult.Hurt, thrower, at, 0, rod: true, sprintKnockback: false, RodKnockback(1.0), new Random(1));
        before.Knock(copy => CombatRules.VelocityPacket(
            CombatRules.Knockback(copy, thrower, at, 0, baseKnockback: true, sprintBonus: false, new Random(1), Tuning.McComboKnockback)));
        int differ = 0;
        double furthestBack = at.Z;
        for (int s = 0; s < 90; s++)
        {
            twist.Step(ChargeTheThrower);
            before.Step(ChargeTheThrower);
            differ += Same(twist, before) ? 0 : 1;
            furthestBack = Math.Min(furthestBack, twist.Position.Z);
        }
        CheckEqual(differ, 0, "ticks where 100% differs from the rod knockback before the twist");
        Check(furthestBack < at.Z - 0.3, $"the knockback landed: pushed back {at.Z - furthestBack:0.00} blocks while charging the thrower");
    }

    public void TestRodKnockbackOffSendsNothingButTheCatchCounts()
    {
        VoxelWorld world = FlatWorld();
        var thrower = new Vec3d(32.5, FloorY, 55.5);
        PlayerMotor caught = Settled(world, new Vec3d(32.5, FloorY, 48.5));
        PlayerMotor untouched = Settled(world, new Vec3d(32.5, FloorY, 48.5));
        for (int s = 0; s < 20; s++)
        {
            caught.Step(ChargeTheThrower);
            untouched.Step(ChargeTheThrower);
        }
        var vitality = new Vitality();
        HitResult result = vitality.TakeHit(Tuning.RodDamage, Tuning.KitArmorPoints, 100);
        CheckEqual(result, HitResult.Hurt, "the catch still counts as a full hit");
        CombatRules.ApplyKnockback(caught, result, thrower, caught.Position, 0, rod: true, sprintKnockback: false, RodKnockback(0), new Random(1));
        Check(!caught.KnockbackFlight, "not flying from a knockback (so pushing still treats them as walking)");
        int differ = 0;
        for (int s = 0; s < 90; s++)
        {
            caught.Step(ChargeTheThrower);
            untouched.Step(ChargeTheThrower);
            differ += Same(caught, untouched) ? 0 : 1;
        }
        CheckEqual(differ, 0, "ticks where a rod at Off changed anything (no velocity packet, the server's copy as it was)");

        // Its hit protection started as with any rod catch.
        CheckEqual(vitality.TakeHit(Tuning.RodDamage, Tuning.KitArmorPoints, 103), HitResult.Ignored,
            "another rod inside the half second flies through");
        CheckEqual(vitality.TakeHit(Tuning.SwordDamage, Tuning.KitArmorPoints, 105), HitResult.Stronger,
            "a sword hit right after does its damage, but no knockback");
        CheckEqual(vitality.TakeHit(Tuning.RodDamage, Tuning.KitArmorPoints, 110), HitResult.Hurt, "half a second later a rod catches again");
    }

    public void TestSwordKnockbackIgnoresTheRodKnockback()
    {
        VoxelWorld world = FlatWorld();
        var attacker = new Vec3d(32.5, FloorY, 51.3);
        foreach (HitResult result in new[] { HitResult.Hurt, HitResult.Stronger })
        {
            foreach (bool sprint in new[] { false, true })
            {
                // As before the twist (Game.ResolveAttack's knockback, inline).
                PlayerMotor before = Settled(world, new Vec3d(32.5, FloorY, 48.5));
                for (int s = 0; s < 20; s++)
                {
                    before.Step(ChargeTheThrower);
                }
                Vec3d at = before.Position;
                if (result == HitResult.Hurt)
                {
                    before.Knock(copy => CombatRules.VelocityPacket(
                        CombatRules.Knockback(copy, attacker, at, 0, baseKnockback: true, sprint, new Random(1), Tuning.McComboKnockback)));
                }
                else if (sprint)
                {
                    before.PushServerCopy(CombatRules.Knockback(Vec3d.Zero, attacker, at, 0, false, true, new Random(1), Tuning.McComboKnockback));
                }
                var after = new List<PlayerMotor>();
                foreach (double rodKnockback in new[] { 0.0, 0.5, 1.0, 1.5, 2.0 })
                {
                    PlayerMotor victim = Settled(world, new Vec3d(32.5, FloorY, 48.5));
                    for (int s = 0; s < 20; s++)
                    {
                        victim.Step(ChargeTheThrower);
                    }
                    CombatRules.ApplyKnockback(victim, result, attacker, victim.Position, 0, rod: false, sprint, RodKnockback(rodKnockback), new Random(1));
                    after.Add(victim);
                }
                int differ = 0;
                for (int s = 0; s < 90; s++)
                {
                    before.Step(ChargeTheThrower);
                    foreach (PlayerMotor victim in after)
                    {
                        victim.Step(ChargeTheThrower);
                        differ += Same(victim, before) ? 0 : 1;
                    }
                }
                CheckEqual(differ, 0, $"ticks where a sword hit ({result}, sprint {sprint}) moved differently at any rod knockback");
            }
        }
    }

    /// <summary>
    /// The whole catch, as the game does it: a level throw from 5 blocks away at a player standing
    /// still, the thrower's game finding the catch frame by frame (Game.StepRods), the victim's
    /// game counting it for hit protection and applying the knockback (Game.ResolveAttack), which
    /// lands at the victim's next Minecraft tick. Returns how far the victim is carried, where
    /// they first touch down and where they come to a stop, and how high they rise.
    /// </summary>
    private static (double Landed, double Stopped, double Height, HitResult Result) RodCatchFlight(double strength)
    {
        VoxelWorld world = FlatWorld();
        var throwerFeet = new Vec3d(32.5, FloorY, 55.5);
        PlayerMotor victim = Settled(world, new Vec3d(32.5, FloorY, 50.5));
        var vitality = new Vitality();
        Bobber bobber = Cast(world, throwerFeet, 0, 0);
        var targets = new List<RodTarget>();
        Vec3d start = victim.Position;
        HitResult result = HitResult.Ignored;
        double height = 0;
        double? landed = null;
        bool airborne = false;
        for (int frame = 0; frame < 4 * Tuning.SimRate; frame++)
        {
            double from = CastTime + frame * Frame;
            targets.Clear();
            targets.Add(new RodTarget(2, victim.Position));
            if (bobber.Catch(from, from + Frame, targets) is { } hit)
            {
                result = vitality.TakeHit(Tuning.RodDamage, Tuning.KitArmorPoints, HitProtection.TickAt(hit.Time));
                start = victim.Position;
                CombatRules.ApplyKnockback(
                    victim, result, throwerFeet, victim.Position, 0, rod: true, sprintKnockback: false, RodKnockback(strength), new Random(1));
                bobber.Hook(2, hit.Time);
            }
            victim.Step(Idle);
            height = Math.Max(height, victim.Position.Y - FloorY);
            airborne |= !victim.OnGround;
            if (airborne && victim.OnGround && landed is null)
            {
                landed = (victim.Position - start).HorizontalLength;
            }
        }
        double stopped = (victim.Position - start).HorizontalLength;
        return (landed ?? stopped, stopped, height, result);
    }

    public void TestRodKnockbackFlightThroughARealCatch()
    {
        // The numbers in docs/rod.md: where a player standing still first touches down, where they
        // stop, and how high their feet rise. More push also means more lift, so more time in the
        // air: the flight grows faster than the percentage.
        foreach (var (strength, landsAt, stopsAt, rises) in new[]
        {
            (0.0, 0.0, 0.0, 0.0),
            (0.5, 0.68, 0.80, 0.25),
            (1.0, 1.83, 1.98, 0.97),
            (1.5, 3.30, 3.45, 2.11),
            (2.0, 4.87, 5.00, 3.63),
        })
        {
            var (landed, stopped, height, result) = RodCatchFlight(strength);
            CheckEqual(result, HitResult.Hurt, $"{strength:P0}: the catch counts");
            CheckNear(landed, landsAt, 0.01, $"{strength:P0}: touches down this far away");
            CheckNear(stopped, stopsAt, 0.01, $"{strength:P0}: stops this far away");
            CheckNear(height, rises, 0.01, $"{strength:P0}: rises this high");
        }
    }
}
