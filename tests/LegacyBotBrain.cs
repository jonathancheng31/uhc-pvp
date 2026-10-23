using System;
using System.Collections.Generic;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Gameplay;
using UhcPvp.Movement;

namespace UhcPvp.Tests;

/// <summary>
/// The fighting bot as it was up to 0.4.6, kept as a yardstick for the new one (tests/BotDuel.cs):
/// it turns at up to 540 degrees a second straight at your chest with a little wobble (so its
/// aim is almost perfect and instant), sprints at you, strafes when within 5 blocks (switching
/// every 0.4 to 1.2 s), sprint-jumps from over 6 blocks, clicks about 9 times a second within
/// 4.2 blocks, w-taps for 2 Minecraft ticks after every sprint hit, and throws its rod at 3.5 to
/// 8 blocks about every 1.5 seconds.
/// </summary>
public sealed class LegacyBotBrain : IDuelBrain
{
    private static readonly int N = Tuning.SubticksPerMcTick;
    private const double TurnSpeed = 540;
    private const double AimWobble = 1.5;
    private const double ClicksPerSecond = 9.0;
    private const double RodSeconds = 1.5;

    private readonly Random _random;
    private double _time;
    private double _strafeTimer;
    private int _strafeDirection = 1;
    private double _clickTimer;
    private double _rodTimer;
    private double _nextJump;
    private int _releaseForwardTicks;

    private readonly BotSkill? _aimLike;
    private readonly BotAim _aim = new();
    private readonly BotPerception _seen = new();

    public LegacyBotBrain(int seed) => _random = new Random(seed);

    /// <summary>
    /// The old bot's decisions with a person's eyes and hands: it sees and aims exactly like
    /// today's bot at <paramref name="aimLike"/>'s skill (same reaction, lead, wander, turning).
    /// Pitting it against today's bot at that skill compares the decisions alone.
    /// </summary>
    public LegacyBotBrain(int seed, BotSkill aimLike, string name)
    {
        _random = new Random(seed);
        _aimLike = aimLike;
        Name = name;
    }

    public string Name { get; } = "0.4.6 bot";

    /// <summary>Up to 0.4.6 a bot clicked after moving.</summary>
    public bool ClicksBeforeMoving => false;

    public void OnAttack(int? targetId, HitResult? result, bool sprintKnockback)
    {
        if (result is HitResult hit && hit != HitResult.Ignored && sprintKnockback)
        {
            _releaseForwardTicks = 2 * N;
        }
    }

    public BotIntent Think(in BotSelf self, IReadOnlyList<BotFoe> foes, MatchRules rules, double delta)
    {
        _time += delta;
        BotFoe? nearest = null;
        double best = double.MaxValue;
        foreach (BotFoe foe in foes)
        {
            double d = (foe.Feet - self.Feet).HorizontalLength;
            if (d < best)
            {
                best = d;
                nearest = foe;
            }
        }
        if (nearest is not BotFoe target)
        {
            return new BotIntent(new MovementInput(0, 0, false, false, false, self.Yaw), false, Pitch: self.Pitch);
        }
        Vec3d eye = self.Feet + new Vec3d(0, Tuning.EyeHeight, 0);
        double yaw;
        double pitch;
        if (_aimLike is BotSkill skill)
        {
            // A person's eyes and hands: see the target late, lead it, aim with lag and wander.
            _seen.Observe(_time, target.Feet);
            target = target with { Feet = _seen.Perceived(_time, skill.ReactionSeconds, skill.Anticipation, Tuning.BotMotionWindow) };
            (double wy, double wp) = BotBrain.LookAngles(eye, target.Feet + new Vec3d(0, Tuning.BotAimHeight, 0));
            (yaw, pitch) = _aim.Step(self.Yaw, self.Pitch, wy, wp, skill, delta, _random);
        }
        else
        {
            // Face: turn toward the chest, with wobble.
            (double wantYaw, double wantPitch) = BotBrain.LookAngles(eye, target.Feet + new Vec3d(0, 1.2, 0));
            double maxTurn = TurnSpeed * delta;
            double wobble = (_random.NextDouble() - 0.5) * 2 * AimWobble * Math.Min(delta * 20, 1);
            yaw = self.Yaw + Math.Clamp(BotAim.WrapDegrees(wantYaw - self.Yaw), -maxTurn, maxTurn) + wobble;
            pitch = self.Pitch + Math.Clamp(wantPitch - self.Pitch, -maxTurn, maxTurn);
        }

        Vec3d offset = target.Feet - self.Feet;
        double distance = Math.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
        double forward = distance > 1.2 ? 1 : 0;
        if (_releaseForwardTicks > 0)
        {
            _releaseForwardTicks--;
            forward = 0;
        }
        double strafe = 0;
        if (distance < 5)
        {
            _strafeTimer -= delta;
            if (_strafeTimer <= 0)
            {
                _strafeDirection = -_strafeDirection;
                _strafeTimer = 0.4 + _random.NextDouble() * 0.8;
            }
            strafe = _strafeDirection;
        }
        bool jump = false;
        if (distance > 6 && _time >= _nextJump)
        {
            jump = true;
            _nextJump = _time + 0.6 + _random.NextDouble() * 0.5;
        }
        bool attack = false;
        _clickTimer -= delta;
        if (distance < 4.2 && _clickTimer <= 0)
        {
            attack = true;
            _clickTimer = 1.0 / (ClicksPerSecond * (0.8 + _random.NextDouble() * 0.4));
        }
        bool rod = false;
        _rodTimer -= delta;
        if (distance > 3.5 && distance < 8.0 && _rodTimer <= 0)
        {
            rod = true;
            _rodTimer = RodSeconds * (0.7 + _random.NextDouble() * 0.6);
        }
        return new BotIntent(new MovementInput(forward, strafe, jump, true, false, yaw), attack, rod, pitch);
    }
}
