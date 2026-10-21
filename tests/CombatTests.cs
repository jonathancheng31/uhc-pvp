using System;
using System.Collections.Generic;
using Godot;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Tests;

public class CombatRuleTests : TestCase
{
    private static readonly int N = Tuning.SubticksPerMcTick;

    public void TestDamageAndArmorMatchMinecraft()
    {
        CheckNear(CombatRules.RawDamage(Tuning.SwordDamage, false), 8.0, 1e-12, "diamond sword");
        CheckNear(CombatRules.RawDamage(Tuning.SwordDamage, true), 12.0, 1e-12, "critical hit");
        CheckNear(CombatRules.AfterArmor(8, 0), 8.0, 1e-12, "no armor");
        CheckNear(CombatRules.AfterArmor(8, 20), 1.6, 1e-12, "full diamond (20 points)");
        CheckNear(CombatRules.AfterArmor(8, Tuning.KitArmorPoints), 2.24, 1e-12, "kit armor");
    }

    public void TestHitProtectionMatchesMinecraft()
    {
        var v = new Vitality();
        CheckEqual(v.TakeHit(8, 0), HitResult.Hurt, "first hit");
        CheckNear(v.Health, 12, 1e-12, "health after hit");
        for (int i = 0; i < 10 * N - 1; i++)
        {
            v.Tick();
        }
        CheckEqual(v.TakeHit(8, 0), HitResult.Ignored, "same damage just before half a second: ignored");
        CheckEqual(v.TakeHit(12, 0), HitResult.Stronger, "a stronger hit during protection counts");
        CheckNear(v.Health, 8, 1e-12, "only the extra 4 damage applied");
        v.Tick();
        CheckEqual(v.TakeHit(8, 0), HitResult.Hurt, "after exactly 10 MC ticks a new full hit lands");
        CheckNear(v.Health, 0, 1e-12, "dead");
        Check(v.IsDead, "is dead");
        CheckEqual(v.TakeHit(8, 0), HitResult.Ignored, "can't hit the dead");
        v.Reset();
        CheckNear(v.Health, Tuning.MaxHealth, 1e-12, "respawn heals");
    }

    public void TestKitTakesNineSwordHitsToKill()
    {
        var v = new Vitality();
        int hits = 0;
        while (!v.IsDead)
        {
            v.TakeHit(Tuning.SwordDamage, Tuning.KitArmorPoints);
            hits++;
            for (int i = 0; i < 10 * N; i++)
            {
                v.Tick();
            }
        }
        CheckEqual(hits, 9, "sword hits to kill a player in the kit");
    }

    public void TestKnockbackFormula()
    {
        var random = new Random(1);
        // Attacker due north (-Z) of a standing victim; not sprinting.
        Vec3d m = CombatRules.Knockback(new Vec3d(0.2, -0.0784, 0), new Vec3d(0, 0, -2), Vec3d.Zero, 0, true, false, random);
        CheckNear(m.X, 0.1, 1e-12, "half the old sideways motion");
        CheckNear(m.Y, 0.3608, 1e-12, "half the old vertical motion plus 0.4");
        CheckNear(m.Z, 0.4, 1e-12, "pushed 0.4 away from the attacker");
        Vec3d capped = CombatRules.Knockback(new Vec3d(0, 0.3, 0), new Vec3d(0, 0, -2), Vec3d.Zero, 0, true, false, random);
        CheckNear(capped.Y, 0.4, 1e-12, "upward knockback capped at 0.4");
        Vec3d sprint = CombatRules.Knockback(Vec3d.Zero, new Vec3d(0, 0, 2), Vec3d.Zero, 0, true, true, random);
        // Attacker south (+Z) facing north (yaw 0 faces -Z): both pushes point to -Z.
        CheckNear(sprint.Z, -0.9, 1e-12, "sprint hit adds 0.5 along the attacker's facing");
        CheckNear(sprint.Y, 0.5, 1e-12, "sprint hit adds 0.1 up (after the cap)");
    }
}

public class CombatMotionTests : TestCase
{
    private const int N = 3;
    private static readonly Vec3d Start = new(32.5, 4, 32.5);

    private static VoxelWorld FlatWorld()
    {
        var world = new VoxelWorld(new Vector3I(64, 24, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, 3, 63), Block.Stone);
        return world;
    }

    private static (McReference Mc, PlayerMotor Ours) Pair()
    {
        VoxelWorld world = FlatWorld();
        var mc = new McReference(world, Start);
        var ours = new PlayerMotor(world, Start);
        // Let both settle on the floor (a brand-new Minecraft player skips ground contact for a tick).
        for (int t = 0; t < 5; t++)
        {
            Tick(mc, ours, new MovementInput(0, 0, false, false, false, 0));
        }
        return (mc, ours);
    }

    private static void Tick(McReference mc, PlayerMotor ours, MovementInput input)
    {
        mc.Tick(input);
        for (int s = 0; s < N; s++)
        {
            ours.Step(input);
        }
    }

    private void CheckTrack(McReference mc, PlayerMotor ours, int ticks, MovementInput input, string what)
    {
        double worst = 0;
        for (int t = 0; t < ticks; t++)
        {
            Tick(mc, ours, input);
            Vec3d d = mc.Position - ours.Position;
            worst = Math.Max(worst, Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z));
        }
        Check(worst < 1e-9, $"{what}: off by {worst:E2} blocks");
    }

    public void TestKnockbackFlightMatchesMinecraftExactly()
    {
        var random = new Random(3);
        foreach (var (name, input, sprintBonus, setupTicks) in new (string, MovementInput, bool, int)[]
        {
            ("standing victim, normal hit", new MovementInput(0, 0, false, false, false, 0), false, 10),
            ("standing victim, sprint hit", new MovementInput(0, 0, false, false, false, 0), true, 10),
            ("victim sprinting at the attacker", new MovementInput(1, 0, false, true, false, 180), true, 20),
            ("victim hit mid-air, holding back", new MovementInput(-1, 0, true, false, false, 0), true, 14),
        })
        {
            var (mc, ours) = Pair();
            for (int t = 0; t < setupTicks; t++)
            {
                Tick(mc, ours, input);
            }
            Vec3d attacker = mc.Position + new Vec3d(0, 0, 2.5);
            Vec3d mcMotion = CombatRules.Knockback(mc.Motion, attacker, mc.Position, 0, true, sprintBonus, random);
            Vec3d ourMotion = CombatRules.Knockback(ours.McMotion, attacker, ours.Position, 0, true, sprintBonus, random);
            CheckNear((mcMotion - ourMotion).HorizontalLength + Math.Abs(mcMotion.Y - ourMotion.Y), 0, 1e-9, $"{name}: same motion before knockback");
            mc.Motion = mcMotion;
            ours.SetMcMotion(ourMotion);
            CheckTrack(mc, ours, 30, input, name);
        }
    }

    public void TestSprintHitSlowdownMatchesMinecraft()
    {
        var (mc, ours) = Pair();
        var sprint = new MovementInput(1, 0, false, true, false, 0);
        for (int t = 0; t < 20; t++)
        {
            Tick(mc, ours, sprint);
        }
        mc.SprintHitSlowdown();
        ours.OnHitLanded();
        Check(!ours.Sprinting, "a sprint hit stops sprinting");
        CheckTrack(mc, ours, 20, sprint, "after a sprint hit, holding sprint");
    }

    public void TestWTapIsNeededForSprintKnockback()
    {
        var (_, ours) = Pair();
        var sprint = new MovementInput(1, 0, false, true, false, 0);
        var release = new MovementInput(0, 0, false, true, false, 0);
        void RunTicks(MovementInput input, int ticks)
        {
            for (int i = 0; i < ticks * N; i++)
            {
                ours.Step(input);
            }
        }
        RunTicks(sprint, 10);
        Check(ours.OnHitLanded(), "first sprint hit gets sprint knockback");
        RunTicks(sprint, 10);
        Check(ours.Sprinting, "holding W and sprint: sprinting again right away");
        Check(!ours.OnHitLanded(), "but the next hit gets no sprint knockback without a w-tap");
        RunTicks(release, 1);
        RunTicks(sprint, 2);
        Check(ours.OnHitLanded(), "after letting go of W for one tick (w-tap), sprint knockback is back");
    }

    public void TestCriticalHitsNeedFalling()
    {
        var (_, ours) = Pair();
        for (int s = 0; s < 6; s++)
        {
            ours.Step(new MovementInput(0, 0, false, false, false, 0));
        }
        Check(!ours.CanCrit, "no crit standing");
        ours.Step(new MovementInput(0, 0, true, false, false, 0));
        for (int s = 0; s < 6; s++)
        {
            ours.Step(new MovementInput(0, 0, false, false, false, 0));
        }
        Check(!ours.CanCrit, "no crit while rising");
        for (int s = 0; s < 15; s++)
        {
            ours.Step(new MovementInput(0, 0, false, false, false, 0));
        }
        Check(ours.CanCrit, "crit while falling");
    }
}

public class HitScanTests : TestCase
{
    private static VoxelWorld World()
    {
        var world = new VoxelWorld(new Vector3I(64, 24, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, 3, 63), Block.Stone);
        return world;
    }

    private static readonly Vec3d Eye = new(32.5, 4 + Tuning.EyeHeight, 32.5);
    private static readonly Vec3d North = HitScan.LookVector(0, 0);

    private static List<AimTarget> TargetAt(double distanceNorth) =>
        [new AimTarget(7, new Vec3d(32.5, 4, 32.5 - distanceNorth))];

    public void TestHitsWithinReachOnly()
    {
        VoxelWorld world = World();
        // Hitbox front face is 0.3 + 0.1 from the target's center.
        AimResult near = HitScan.Aim(Eye, North, TargetAt(3.35), world);
        CheckEqual(near.Kind, AimKind.Player, "2.95 blocks to the hitbox: in reach");
        CheckEqual(near.TargetId, 7, "right target");
        CheckNear(near.Distance, 2.95, 1e-9, "distance to the hitbox");
        CheckEqual(HitScan.Aim(Eye, North, TargetAt(3.45), world).Kind, AimKind.Miss, "3.05 blocks: out of reach, counts as a miss");
    }

    public void TestBorderMakesHitboxSlightlyBigger()
    {
        VoxelWorld world = World();
        // Aim 0.35 to the side of the target's center: outside the 0.3 body, inside the 0.4 hitbox.
        var targets = new List<AimTarget> { new(1, new Vec3d(32.85, 4, 30.5)) };
        CheckEqual(HitScan.Aim(Eye, North, targets, world).Kind, AimKind.Player, "border catches a near miss");
        targets = [new(1, new Vec3d(32.95, 4, 30.5))];
        Check(HitScan.Aim(Eye, North, targets, world).Kind != AimKind.Player, "0.45 off is a miss");
    }

    public void TestWallsBlockHits()
    {
        VoxelWorld world = World();
        world.Fill(new Vector3I(30, 4, 30), new Vector3I(35, 7, 30), Block.Stone); // wall 1.5 blocks north
        AimResult r = HitScan.Aim(Eye, North, TargetAt(2.5), world);
        CheckEqual(r.Kind, AimKind.Block, "target behind a wall can't be hit");
    }

    public void TestLookingAtGroundOrSky()
    {
        VoxelWorld world = World();
        var none = new List<AimTarget>();
        CheckEqual(HitScan.Aim(Eye, HitScan.LookVector(0, -60), none, world).Kind, AimKind.Block, "looking at the ground nearby");
        CheckEqual(HitScan.Aim(Eye, HitScan.LookVector(0, 30), none, world).Kind, AimKind.Miss, "looking at the sky");
    }

    public void TestEyeInsideHitbox()
    {
        VoxelWorld world = World();
        var targets = new List<AimTarget> { new(3, new Vec3d(32.6, 4, 32.5)) };
        AimResult r = HitScan.Aim(Eye, North, targets, world);
        Check(r.Kind == AimKind.Player && r.Distance == 0, "standing inside someone hits them");
    }

    public void TestOutOfReachPlayerInFrontOfBlockIsAMiss()
    {
        VoxelWorld world = World();
        world.Fill(new Vector3I(30, 4, 28), new Vector3I(35, 7, 28), Block.Stone); // wall ~3.5 north
        // Minecraft quirk: pointing at a player beyond 3 blocks overrides the block behind them.
        CheckEqual(HitScan.Aim(Eye, North, TargetAt(3.45), world).Kind, AimKind.Miss, "miss, not block");
    }
}

public class BotAimTests : TestCase
{
    public void TestBotLookAnglesPointAtTarget()
    {
        var world = new VoxelWorld(new Vector3I(64, 24, 64));
        world.Fill(new Vector3I(0, 0, 0), new Vector3I(63, 3, 63), Block.Stone);
        var eye = new Vec3d(32.5, 4 + Tuning.EyeHeight, 32.5);
        var random = new Random(4);
        for (int i = 0; i < 50; i++)
        {
            var feet = new Vec3d(32.5 + random.NextDouble() * 4 - 2, 4, 32.5 + random.NextDouble() * 4 - 2);
            if ((feet - new Vec3d(eye.X, 4, eye.Z)).HorizontalLength < 0.8)
            {
                continue;
            }
            (double yaw, double pitch) = Gameplay.BotBrain.LookAngles(eye, feet + new Vec3d(0, 1.2, 0));
            AimResult aim = HitScan.Aim(eye, HitScan.LookVector(yaw, pitch), [new AimTarget(9, feet)], world);
            CheckEqual(aim.Kind, AimKind.Player, $"aiming at a player at {feet}");
        }
    }
}
