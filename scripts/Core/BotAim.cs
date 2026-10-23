using System;

namespace UhcPvp.Core;

/// <summary>
/// A practice bot's hand on the mouse. It turns toward where the bot wants to look the way a
/// person's aim does: it follows a target that moves across its view at about the target's
/// speed (people track smoothly), closes any remaining gap quickly when far off and gently when
/// close, never turns faster than a top speed, and always wanders a little around the target
/// (a slow drift, not a jitter). A hit on the bot knocks its aim off, as knockback and the hurt
/// camera do to a person.
/// </summary>
public sealed class BotAim
{
    /// <summary>Pitch wanders this much less than yaw (people keep their crosshair level more easily).</summary>
    private const double PitchWanderScale = 0.6;
    /// <summary>How long the bot takes to pick up a change in how fast its target crosses its view, in seconds.</summary>
    private const double PursuitSmoothing = 0.08;
    /// <summary>A target that jumps more than this between ticks (a new target, a respawn) isn't followed, just turned to.</summary>
    private const double PursuitJumpDegrees = 20;

    private double? _lastTargetYaw;
    private double _lastTargetPitch;
    private double _pursuitYaw;
    private double _pursuitPitch;

    /// <summary>The wander, in degrees: how far off the aim currently means to be.</summary>
    public double WanderYaw { get; private set; }
    public double WanderPitch { get; private set; }

    /// <summary>
    /// One tick: from (<paramref name="yaw"/>, <paramref name="pitch"/>) toward
    /// (<paramref name="targetYaw"/>, <paramref name="targetPitch"/>) plus the wander. Degrees.
    /// </summary>
    public (double Yaw, double Pitch) Step(
        double yaw, double pitch, double targetYaw, double targetPitch, BotSkill skill, double delta, Random random)
    {
        Wander(skill.AimErrorDegrees, delta, random);
        Pursue(targetYaw, targetPitch, delta);
        double wantYaw = targetYaw + WanderYaw;
        double wantPitch = targetPitch + WanderPitch;
        // Move with the target, then close most of the remaining gap within the settle time (an
        // exponential ease), capped by the turn speed.
        double ease = 1 - Math.Exp(-delta / Math.Max(skill.AimSettleSeconds, 1e-3));
        double maxTurn = skill.TurnSpeed * delta;
        double followYaw = _pursuitYaw * delta;
        double followPitch = _pursuitPitch * delta;
        double dYaw = Math.Clamp(followYaw + WrapDegrees(wantYaw - yaw - followYaw) * ease, -maxTurn, maxTurn);
        double dPitch = Math.Clamp(followPitch + (wantPitch - pitch - followPitch) * ease, -maxTurn, maxTurn);
        return (WrapDegrees(yaw + dYaw), Math.Clamp(pitch + dPitch, -90, 90));
    }

    /// <summary>A hit on the bot throws its aim off by about <paramref name="degrees"/>.</summary>
    public void Jolt(double degrees, Random random)
    {
        WanderYaw += Gaussian(random) * degrees;
        WanderPitch += Gaussian(random) * degrees * 0.5;
    }

    /// <summary>Tracks how fast the target crosses the view (degrees per second), smoothed like a person's eye.</summary>
    private void Pursue(double targetYaw, double targetPitch, double delta)
    {
        if (_lastTargetYaw is double lastYaw && delta > 0)
        {
            double dYaw = WrapDegrees(targetYaw - lastYaw);
            double dPitch = targetPitch - _lastTargetPitch;
            if (Math.Abs(dYaw) > PursuitJumpDegrees || Math.Abs(dPitch) > PursuitJumpDegrees)
            {
                _pursuitYaw = 0;
                _pursuitPitch = 0;
            }
            else
            {
                double follow = 1 - Math.Exp(-delta / PursuitSmoothing);
                _pursuitYaw += (dYaw / delta - _pursuitYaw) * follow;
                _pursuitPitch += (dPitch / delta - _pursuitPitch) * follow;
            }
        }
        _lastTargetYaw = targetYaw;
        _lastTargetPitch = targetPitch;
    }

    /// <summary>
    /// The wander drifts like a slow random walk pulled back toward zero (an Ornstein-Uhlenbeck
    /// process): it stays about <paramref name="degrees"/> off on average and changes over
    /// <see cref="Tuning.BotAimWanderSeconds"/>.
    /// </summary>
    private void Wander(double degrees, double delta, Random random)
    {
        double keep = Math.Exp(-delta / Tuning.BotAimWanderSeconds);
        double kick = Math.Sqrt(1 - keep * keep);
        WanderYaw = WanderYaw * keep + Gaussian(random) * degrees * kick;
        WanderPitch = WanderPitch * keep + Gaussian(random) * degrees * PitchWanderScale * kick;
    }

    /// <summary>An angle in degrees, wrapped to -180..180.</summary>
    public static double WrapDegrees(double a)
    {
        a %= 360;
        return a > 180 ? a - 360 : a < -180 ? a + 360 : a;
    }

    /// <summary>A standard normal random number (Box-Muller).</summary>
    public static double Gaussian(Random random)
    {
        double u = 1.0 - random.NextDouble();
        double v = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
    }
}
