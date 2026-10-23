using System;
using System.Collections.Generic;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Gameplay;
using UhcPvp.Movement;

namespace UhcPvp.Tests;

/// <summary>
/// A scripted attacker standing in for a player who w-taps well, to measure how comboable each
/// bot is (tests/BotReport.cs, tests/BotTests.cs): it sprints straight at the target, w-taps
/// for one Minecraft tick after every sprint hit, never clicks into their hit protection (so it
/// never slows itself) and, once it's over, clicks the moment its crosshair is on them within
/// reach (at most 12 times a second). No rod, no strafing, no tricks. By default it aims
/// perfectly and instantly; given a skill, it sees and aims like a person at that skill
/// (<see cref="BotPerception"/>, <see cref="BotAim"/>), so it also misses sometimes.
/// </summary>
public sealed class ComboAttacker : IDuelBrain
{
    private static readonly int N = Tuning.SubticksPerMcTick;
    private const double ClicksPerSecond = 12;
    /// <summary>Hit protection after a full hit, on the same 1/20 s grid the game uses: 10 ticks.</summary>
    private const double Protection = 0.5;

    private readonly BotSkill? _aimLike;
    private readonly BotAim _aim = new();
    private readonly BotPerception _seen = new();
    private readonly Random _random;
    private double _nextClick;
    private double _time;
    private int _tapTicks;

    public ComboAttacker()
    {
        _random = new Random(1);
        Name = "perfect w-tapping attacker";
    }

    public ComboAttacker(BotSkill aimLike, int seed)
    {
        _aimLike = aimLike;
        _random = new Random(seed);
        Name = "w-tapping player";
    }

    public string Name { get; }

    public void OnAttack(int? targetId, HitResult? result, bool sprintKnockback)
    {
        if (result == HitResult.Hurt && sprintKnockback)
        {
            _tapTicks = N;
        }
    }

    public BotIntent Think(in BotSelf self, IReadOnlyList<BotFoe> foes, MatchRules rules, double delta)
    {
        _time += delta;
        if (foes.Count == 0)
        {
            return new BotIntent(new MovementInput(0, 0, false, true, false, self.Yaw), false, Pitch: self.Pitch);
        }
        BotFoe foe = foes[0];
        Vec3d feet = foe.Feet;
        Vec3d eye = self.Feet + new Vec3d(0, Tuning.EyeHeight, 0);
        double yaw;
        double pitch;
        if (_aimLike is BotSkill skill)
        {
            _seen.Observe(_time, foe.Feet);
            feet = _seen.Perceived(_time, skill.ReactionSeconds, skill.Anticipation, Tuning.BotMotionWindow);
            (double wy, double wp) = BotBrain.LookAngles(eye, feet + new Vec3d(0, Tuning.BotAimHeight, 0));
            (yaw, pitch) = _aim.Step(self.Yaw, self.Pitch, wy, wp, skill, delta, _random);
        }
        else
        {
            (yaw, pitch) = BotBrain.LookAngles(eye, feet + new Vec3d(0, Tuning.BotAimHeight, 0));
        }
        double forward = 1;
        if (_tapTicks > 0)
        {
            _tapTicks--;
            forward = 0;
        }
        double distance = (feet - self.Feet).HorizontalLength;
        // It clicks the moment its crosshair is on them (as it sees them) within reach.
        bool onTarget = HitScan.Hitbox(feet).RayEntry(eye, HitScan.LookVector(yaw, pitch), Tuning.Reach) is not null;
        // Their protection, timed from the red flash (with a hair to spare for the tick grid).
        bool open = foe.SinceHurt >= Protection + 0.005;
        bool attack = onTarget && open && _time >= _nextClick && self.CanClick;
        if (attack)
        {
            _nextClick = _time + 1 / ClicksPerSecond;
        }
        bool jump = distance > Tuning.BotSprintJumpDistance;
        return new BotIntent(new MovementInput(forward, 0, jump, true, false, yaw), attack, false, pitch);
    }
}
