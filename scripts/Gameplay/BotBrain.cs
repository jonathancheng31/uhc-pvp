using System;
using System.Collections.Generic;
using UhcPvp.Core;
using UhcPvp.Movement;

namespace UhcPvp.Gameplay;

/// <summary>How a practice bot behaves.</summary>
public enum BotMode
{
    /// <summary>Stands still and faces the nearest enemy: a training dummy.</summary>
    Still,
    /// <summary>Strafes left and right while facing you: a moving target.</summary>
    Strafe,
    /// <summary>Chases the nearest enemy and fights back: sprinting, strafing, clicking, w-tapping.</summary>
    Fight,
    /// <summary>Moves exactly like <see cref="Fight"/> but never swings: a target that runs at you.</summary>
    Chase,
    /// <summary>Sprints in circles, hops and sneaks (for automated multiplayer tests).</summary>
    Wander,
}

/// <summary>What a bot wants to do this tick: which keys it holds and whether it clicks.</summary>
public readonly record struct BotIntent(MovementInput Input, bool Attack);

/// <summary>
/// Practice-bot AI. It "plays" through the same controls a person does: it turns its view,
/// presses movement keys and clicks, and its hits go through exactly the same rules.
/// </summary>
public sealed class BotBrain
{
    private static readonly int N = Tuning.SubticksPerMcTick;

    /// <summary>How fast the bot can turn its view, in degrees per second.</summary>
    private const double TurnSpeed = 540;
    /// <summary>Random aim wobble, in degrees.</summary>
    private const double AimWobble = 1.5;

    private readonly Random _random;
    private double _time;
    private double _strafeTimer;
    private int _strafeDirection = 1;
    private double _clickTimer;
    private double _nextJump;
    private int _releaseForwardTicks;

    public BotBrain(BotMode mode, int seed)
    {
        Mode = mode;
        _random = new Random(seed);
    }

    public BotMode Mode { get; set; }

    /// <summary>
    /// The bot's sprint hit landed: let go of W for a moment (a w-tap) so its next hit gets
    /// sprint knockback again, like a good 1.8 player.
    /// </summary>
    public void OnSprintHitLanded() => _releaseForwardTicks = 2 * N;

    public BotIntent Think(Player self, IReadOnlyList<Player> enemies, double delta)
    {
        _time += delta;
        Player? target = Nearest(self, enemies);
        return Mode switch
        {
            BotMode.Still => Still(self, target, delta),
            BotMode.Strafe => Strafe(self, target, delta),
            BotMode.Fight => Fight(self, target, delta, clicks: true),
            BotMode.Chase => Fight(self, target, delta, clicks: false),
            _ => Wander(self, delta),
        };
    }

    private BotIntent Still(Player self, Player? target, double delta)
    {
        Face(self, target, delta);
        return new BotIntent(new MovementInput(0, 0, false, false, false, self.Yaw), false);
    }

    private BotIntent Strafe(Player self, Player? target, double delta)
    {
        Face(self, target, delta);
        SwitchStrafe(delta, 0.8, 1.4);
        return new BotIntent(new MovementInput(0, _strafeDirection, false, false, false, self.Yaw), false);
    }

    private BotIntent Fight(Player self, Player? target, double delta, bool clicks)
    {
        if (target is null)
        {
            return new BotIntent(new MovementInput(0, 0, false, false, false, self.Yaw), false);
        }
        Face(self, target, delta);
        Vec3d offset = target.ShownFeet - self.Feet;
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
            SwitchStrafe(delta, 0.4, 1.2);
            strafe = _strafeDirection;
        }
        bool jump = false;
        if (distance > 6 && _time >= _nextJump)
        {
            // Sprint-jump to close the gap, like players do.
            jump = true;
            _nextJump = _time + 0.6 + _random.NextDouble() * 0.5;
        }

        bool attack = false;
        _clickTimer -= delta;
        if (clicks && distance < 4.2 && _clickTimer <= 0)
        {
            attack = true;
            double cps = Tuning.BotClicksPerSecond * (0.8 + _random.NextDouble() * 0.4);
            _clickTimer = 1.0 / cps;
        }
        return new BotIntent(new MovementInput(forward, strafe, jump, true, false, self.Yaw), attack);
    }

    private BotIntent Wander(Player self, double delta)
    {
        self.Look(self.Yaw + (float)(60 * delta), 0);
        double cycle = _time % 6.0;
        bool sneak = cycle is > 4.5 and < 5.5;
        bool jump = !sneak && _time % 1.5 < 0.1;
        double strafe = cycle is > 2 and < 3 ? 1 : 0;
        return new BotIntent(new MovementInput(1, strafe, jump, !sneak, sneak, self.Yaw), false);
    }

    private void SwitchStrafe(double delta, double minSeconds, double maxSeconds)
    {
        _strafeTimer -= delta;
        if (_strafeTimer <= 0)
        {
            _strafeDirection = -_strafeDirection;
            _strafeTimer = minSeconds + _random.NextDouble() * (maxSeconds - minSeconds);
        }
    }

    /// <summary>Turns toward the target's chest at a human-ish speed, with a little wobble.</summary>
    private void Face(Player self, Player? target, double delta)
    {
        if (target is null)
        {
            return;
        }
        Vec3d chest = target.ShownFeet + new Vec3d(0, 1.2, 0);
        (double yaw, double pitch) = LookAngles(self.Eye, chest);
        double maxTurn = TurnSpeed * delta;
        double wobble = (_random.NextDouble() - 0.5) * 2 * AimWobble * Math.Min(delta * 20, 1);
        double dYaw = Math.Clamp(WrapDegrees(yaw - self.Yaw), -maxTurn, maxTurn) + wobble;
        double dPitch = Math.Clamp(pitch - self.Pitch, -maxTurn, maxTurn);
        self.Look((float)(self.Yaw + dYaw), (float)(self.Pitch + dPitch));
    }

    /// <summary>Yaw and pitch (degrees) that look from <paramref name="eye"/> at <paramref name="point"/>.</summary>
    public static (double Yaw, double Pitch) LookAngles(Vec3d eye, Vec3d point)
    {
        Vec3d d = point - eye;
        double yaw = Math.Atan2(-d.X, -d.Z) * 180.0 / Math.PI;
        double pitch = Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)) * 180.0 / Math.PI;
        return (yaw, pitch);
    }

    private static double WrapDegrees(double a)
    {
        a %= 360;
        return a > 180 ? a - 360 : a < -180 ? a + 360 : a;
    }

    private static Player? Nearest(Player self, IReadOnlyList<Player> enemies)
    {
        Player? best = null;
        double bestDistance = double.MaxValue;
        foreach (Player enemy in enemies)
        {
            Vec3d d = enemy.ShownFeet - self.Feet;
            double distance = d.X * d.X + d.Z * d.Z;
            if (distance < bestDistance)
            {
                best = enemy;
                bestDistance = distance;
            }
        }
        return best;
    }
}
