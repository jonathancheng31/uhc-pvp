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
        CheckEqual(v.TakeHit(8, 0, 100), HitResult.Hurt, "first hit");
        CheckNear(v.Health, 12, 1e-12, "health after hit");
        CheckEqual(v.TakeHit(8, 0, 109), HitResult.Ignored, "same damage 9 ticks later: ignored");
        CheckEqual(v.TakeHit(12, 0, 109), HitResult.Stronger, "a stronger hit during protection counts");
        CheckNear(v.Health, 8, 1e-12, "only the extra 4 damage applied");
        CheckEqual(v.TakeHit(12, 0, 109), HitResult.Ignored, "not stronger than the last (crit) hit");
        CheckEqual(v.TakeHit(8, 0, 110), HitResult.Hurt, "10 ticks after the full hit, a new full hit lands");
        CheckNear(v.Health, 0, 1e-12, "dead");
        Check(v.IsDead, "is dead");
        CheckEqual(v.TakeHit(8, 0, 200), HitResult.Ignored, "can't hit the dead");
        v.Reset();
        CheckNear(v.Health, Tuning.MaxHealth, 1e-12, "respawn heals");
        CheckEqual(v.TakeHit(8, 0, 201), HitResult.Hurt, "respawning clears hit protection");
    }

    public void TestHitProtectionCountsOnTheTickGrid()
    {
        // Two clicks land 10 Minecraft ticks apart when the 1/20 s ticks they fall in are 10
        // apart: from 450 ms (first click late in its tick) to 500 ms (early in its tick).
        static HitResult Second(double first, double second)
        {
            var p = new HitProtection();
            p.Hit(8, HitProtection.TickAt(first));
            return p.Hit(8, HitProtection.TickAt(second)).Result;
        }
        CheckEqual(Second(10.049, 10.501), HitResult.Hurt, "452 ms after a click late in its tick");
        CheckEqual(Second(10.001, 10.499), HitResult.Ignored, "498 ms after a click early in its tick");
        CheckEqual(Second(10.001, 10.501), HitResult.Hurt, "500 ms always lands");
        CheckEqual(Second(10.049, 10.449), HitResult.Ignored, "400 ms never lands");
        CheckEqual(HitProtection.TickAt(-0.01), -1L, "ticks round down, also before zero");
    }

    public void TestKitTakesNineSwordHitsToKill()
    {
        var v = new Vitality();
        int hits = 0;
        while (!v.IsDead)
        {
            v.TakeHit(Tuning.SwordDamage, Tuning.KitArmorPoints, hits * 10);
            hits++;
        }
        CheckEqual(hits, 9, "sword hits to kill a player in the kit");
    }

    public void TestVelocityPacketRounding()
    {
        Vec3d v = CombatRules.VelocityPacket(new Vec3d(0.46075, -0.30431, 5));
        CheckNear(v.X, 3686 / 8000.0, 1e-15, "rounded down to 1/8000");
        CheckNear(v.Y, -2434 / 8000.0, 1e-15, "negative values round toward zero");
        CheckNear(v.Z, 31200 / 8000.0, 1e-15, "clamped to 3.9 blocks per tick");
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
            Vec3d knockback = CombatRules.VelocityPacket(
                CombatRules.Knockback(ours.ServerMotion, attacker, ours.Position, 0, true, sprintBonus, random));
            mc.Motion = knockback;
            ours.SetMcMotion(knockback);
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
        ours.OnHitLanded(accepted: true);
        Check(ours.Sprinting, "a sprint hit no longer stops your sprint");
        // 1.8.9 stops it, but restarts it before moving when the sprint key is held, so the
        // movement is identical to keeping it.
        CheckTrack(mc, ours, 20, sprint, "after a sprint hit, holding sprint");
    }

    public void TestSprintHitSlowsOncePerTick()
    {
        // 1.8.9's first click on a player stops sprint until the next tick, so more clicks before
        // that tick don't slow you again; the next tick's clicks do.
        var (mc, ours) = Pair();
        var sprint = new MovementInput(1, 0, false, true, false, 0);
        for (int t = 0; t < 20; t++)
        {
            Tick(mc, ours, sprint);
        }
        for (int click = 0; click < 3; click++)
        {
            mc.SprintHitSlowdown();
            ours.OnHitLanded(accepted: true);
        }
        CheckTrack(mc, ours, 1, sprint, "three clicks before one tick slow once");
        mc.SprintHitSlowdown();
        ours.OnHitLanded(accepted: true);
        CheckTrack(mc, ours, 20, sprint, "a click before the next tick slows again");

        // Our clicks land between steps. The ones after a tick starts and before the next one
        // starts are those 1.8.9 handles together before that next tick's move: they slow once,
        // like a single click.
        var (_, many) = Pair();
        var (_, one) = Pair();
        for (int s = 0; s <= 20 * N; s++)
        {
            many.Step(sprint);
            one.Step(sprint);
        }
        for (int s = 0; s < N; s++)
        {
            many.OnHitLanded(accepted: true);
            if (s == 0)
            {
                one.OnHitLanded(accepted: true);
            }
            many.Step(sprint);
            one.Step(sprint);
        }
        many.OnHitLanded(accepted: true);
        one.OnHitLanded(accepted: true);
        many.Step(sprint);
        one.Step(sprint);
        CheckNear(Distance(many.Position, one.Position) + Distance(many.Velocity, one.Velocity), 0, 1e-12,
            "a click before every step of a tick slows once; the next tick's click slows again");
    }

    public void TestDoubleTapSprintSurvivesHits()
    {
        var (_, ours) = Pair();
        var walk = new MovementInput(1, 0, false, false, false, 0);
        var idle = new MovementInput(0, 0, false, false, false, 0);
        foreach (MovementInput input in new[] { walk, idle, walk })
        {
            for (int i = 0; i < N; i++)
            {
                ours.Step(input);
            }
        }
        for (int i = 0; i < 10 * N; i++)
        {
            ours.Step(walk);
        }
        Check(ours.Sprinting, "double-tapping W sprints");
        ours.OnHitLanded(accepted: true);
        for (int i = 0; i < 10 * N; i++)
        {
            ours.Step(walk);
        }
        Check(ours.Sprinting, "still sprinting after a hit (1.8.9 would stop until the next double-tap)");
        CheckNear(ours.HorizontalSpeed, 5.612, 0.01, "back to full sprint speed");
        Check(!ours.ServerSprinting, "sprint knockback still needs a w-tap");
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
        Check(ours.ServerSprinting, "first sprint hit gets sprint knockback");
        ours.OnHitLanded(accepted: true);
        RunTicks(sprint, 10);
        Check(ours.Sprinting, "holding W and sprint: sprinting again right away");
        Check(!ours.ServerSprinting, "but the next hit gets no sprint knockback without a w-tap");
        RunTicks(release, 1);
        RunTicks(sprint, 2);
        Check(ours.ServerSprinting, "after letting go of W for one tick (w-tap), sprint knockback is back");

        // Clicking a player who is still protected: the server's sprint flag is only used up by
        // hits that count.
        for (int phase = 0; phase < N; phase++)
        {
            ours.OnHitLanded(accepted: false);
            RunTicks(sprint, 1);
            Check(ours.ServerSprinting, $"a click into hit protection keeps sprint knockback (sprint key held, subtick {phase})");
            ours.Step(sprint);
        }
        RunTicks(sprint, 1);
        var tapSprint = new MovementInput(1, 0, false, false, false, 0);
        ours.OnHitLanded(accepted: false);
        RunTicks(tapSprint, 1);
        Check(ours.ServerSprinting, "double-tap sprinting keeps going too, so a click into protection keeps sprint knockback");
    }

    /// <summary>A Minecraft player, our motor and Minecraft's server copy, settled on a floor.</summary>
    private static (McReference Mc, PlayerMotor Ours, McServerCopy Server) WithServer()
    {
        VoxelWorld world = FlatWorld();
        var mc = new McReference(world, Start);
        var ours = new PlayerMotor(world, Start);
        var server = new McServerCopy(world, Start);
        for (int t = 0; t < 5; t++)
        {
            TickWithServer(mc, ours, server, new MovementInput(0, 0, false, false, false, 0));
        }
        return (mc, ours, server);
    }

    private static void TickWithServer(McReference mc, PlayerMotor ours, McServerCopy server, MovementInput input)
    {
        Tick(mc, ours, input);
        server.Report(mc.Position, mc.OnGround, mc.Sprinting, input.Yaw);
    }

    private static double Distance(Vec3d a, Vec3d b)
    {
        Vec3d d = a - b;
        return Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
    }

    public void TestServerCopyMatchesMinecraft()
    {
        foreach (var (name, input) in new (string, MovementInput)[]
        {
            ("standing", new MovementInput(0, 0, false, false, false, 0)),
            ("jumping in place", new MovementInput(0, 0, true, false, false, 0)),
            ("sprint-jumping", new MovementInput(1, 0, true, true, false, 30)),
            ("walking sideways", new MovementInput(0, 1, false, false, false, 0)),
            ("sneaking forward", new MovementInput(1, 0, false, false, true, 0)),
        })
        {
            var (mc, ours, server) = WithServer();
            double worst = 0;
            for (int t = 0; t < 45; t++)
            {
                TickWithServer(mc, ours, server, input);
                worst = Math.Max(worst, Distance(ours.ServerMotion, server.Motion));
            }
            Check(worst < 1e-9, $"{name}: server copy off by {worst:E2}");
        }
        var (_, standing, _) = WithServer();
        CheckNear(standing.ServerMotion.Y, -0.0784, 1e-9, "standing: the copy rests at -0.0784");
        CheckNear(standing.ServerMotion.HorizontalLength, 0, 1e-12, "standing: no sideways motion");
    }

    public void TestComboKnockbackMatchesMinecraft()
    {
        // Sprint hits every 10 ticks on a victim who doesn't touch the keys, from 2.5 blocks
        // behind (+Z) with the attacker facing them (yaw 0 faces -Z).
        var (mc, ours, server) = WithServer();
        var idle = new MovementInput(0, 0, false, false, false, 0);
        var random = new Random(5);
        var lift = new List<double>();
        for (int hit = 0; hit < 3; hit++)
        {
            Vec3d attacker = mc.Position + new Vec3d(0, 0, 2.5);
            Vec3d ourKnockback = CombatRules.VelocityPacket(
                CombatRules.Knockback(ours.ServerMotion, attacker, ours.Position, 0, true, true, random));
            Vec3d mcKnockback = CombatRules.VelocityPacket(
                CombatRules.Knockback(server.Motion, attacker, mc.Position, 0, true, true, random));
            CheckNear(Distance(ourKnockback, mcKnockback), 0, 1e-9, $"hit {hit + 1}: same knockback as Minecraft");
            CheckNear(ourKnockback.HorizontalLength, 0.9, 1e-3, $"hit {hit + 1}: the same 0.9 sideways every hit");
            lift.Add(ourKnockback.Y);
            mc.Motion = mcKnockback;
            ours.SetMcMotion(ourKnockback);
            double worst = 0;
            for (int t = 0; t < 10; t++)
            {
                TickWithServer(mc, ours, server, idle);
                worst = Math.Max(worst, Distance(mc.Position, ours.Position) + Distance(ours.ServerMotion, server.Motion));
            }
            Check(worst < 1e-9, $"after hit {hit + 1}: flight and server copy off by {worst:E2}");
        }
        CheckNear(lift[0], 0.4608, 1e-3, "first hit lifts 0.46");
        Check(lift[1] < lift[0] - 0.05, $"second hit, still airborne, lifts less ({lift[1]:0.000})");
        Check(lift[2] < 0.15, $"third hit lifts hardly at all ({lift[2]:0.000})");
    }

    public void TestStrongerHitPushStaysOnServerCopy()
    {
        var (mc, ours, server) = WithServer();
        var idle = new MovementInput(0, 0, false, false, false, 0);
        Vec3d push = Vec3d.Forward(0) * Tuning.SprintKnockbackBonus + new Vec3d(0, Tuning.SprintHitVerticalBonus, 0);
        Vec3d before = ours.Position;
        ours.PushServerCopy(push);
        server.Motion += push;
        double worst = 0;
        for (int t = 0; t < 6; t++)
        {
            TickWithServer(mc, ours, server, idle);
            worst = Math.Max(worst, Distance(ours.ServerMotion, server.Motion));
        }
        Check(worst < 1e-9, $"the leftover push decays like Minecraft's (off by {worst:E2})");
        CheckNear(Distance(before, ours.Position), 0, 1e-12, "the victim itself didn't move");
        Check(ours.ServerMotion.HorizontalLength > 0.01, "some of the push is left for the next knockback");
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

public class MissPenaltyTests : TestCase
{
    /// <summary>Minecraft.runTick's click handling, with clicks queued until the next tick.</summary>
    private sealed class McClicks
    {
        private readonly List<(int Id, bool Miss)> _queued = new();
        private int _leftClickCounter;
        private bool _held;
        public HashSet<int> Handled { get; } = new();

        public void Press(int id, bool miss)
        {
            _queued.Add((id, miss));
            _held = true;
        }

        public void Release() => _held = false;

        public void Tick()
        {
            if (_leftClickCounter > 0)
            {
                --_leftClickCounter;
            }
            foreach (var (id, miss) in _queued)
            {
                if (_leftClickCounter <= 0)
                {
                    Handled.Add(id);
                    if (miss)
                    {
                        _leftClickCounter = 10;
                    }
                }
            }
            _queued.Clear();
            if (!_held)
            {
                _leftClickCounter = 0; // sendClickBlockToController(false)
            }
        }
    }

    public void TestMatchesMinecraftForAnyClicking()
    {
        int presses = 0;
        int eaten = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            double cps = 6 + random.NextDouble() * 14;
            double maxHold = 0.01 + random.NextDouble() * 0.12;
            var events = new List<(double Time, int Kind, int Id, bool Miss)>(); // kind 0 tick, 1 press, 2 release
            for (double t = 0.0137; t < 6; t += 0.05)
            {
                events.Add((t, 0, -1, false));
            }
            double time = 0;
            for (int id = 0; time < 5.8; id++)
            {
                time += (0.4 + random.NextDouble() * 1.2) / cps;
                double hold = Math.Min(0.005 + random.NextDouble() * maxHold, 0.9 / cps);
                bool miss = random.NextDouble() < 0.4;
                events.Add((time, 1, id, miss));
                events.Add((time + hold, 2, id, false));
            }
            events.Sort((a, b) => a.Time.CompareTo(b.Time));

            var mc = new McClicks();
            var ours = new MissPenalty();
            var ourHandled = new HashSet<int>();
            bool held = false;
            foreach (var (_, kind, id, miss) in events)
            {
                switch (kind)
                {
                    case 0:
                        mc.Tick();
                        ours.Tick(held);
                        break;
                    case 1:
                        presses++;
                        mc.Press(id, miss);
                        held = true;
                        if (!ours.Blocks)
                        {
                            ourHandled.Add(id);
                            if (miss)
                            {
                                ours.Missed();
                            }
                        }
                        break;
                    default:
                        mc.Release();
                        held = false;
                        break;
                }
            }
            mc.Tick();
            Check(mc.Handled.SetEquals(ourHandled), $"seed {seed}: same clicks go through as in Minecraft");
            eaten += presses - mc.Handled.Count;
            presses = 0;
        }
        Check(eaten > 0, "the timelines include clicks the penalty eats");
    }

    public void TestOnlyHeldOrSameTickClicksAreEaten()
    {
        var p = new MissPenalty();
        p.Missed();
        Check(p.Blocks, "a second click in the same tick as the miss is eaten");
        p.Tick(buttonHeld: false);
        Check(!p.Blocks, "letting go clears it at the end of the tick");
        p.Missed();
        for (int t = 0; t < 9; t++)
        {
            p.Tick(buttonHeld: true);
            Check(p.Blocks, $"holding the button: still blocked after {t + 1} ticks");
        }
        p.Tick(buttonHeld: true);
        Check(!p.Blocks, "holding the button: the miss wears off after 10 ticks");
    }
}
