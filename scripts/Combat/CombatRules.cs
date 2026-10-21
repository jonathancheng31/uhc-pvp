using System;
using UhcPvp.Core;
using UhcPvp.Movement;

namespace UhcPvp.Combat;

/// <summary>Damage and knockback math, following Minecraft 1.8.9 (see docs/combat.md).</summary>
public static class CombatRules
{
    /// <summary>A hit's damage before armor: the weapon's damage, times 1.5 for a critical hit.</summary>
    public static double RawDamage(double weaponDamage, bool critical) =>
        critical ? weaponDamage * Tuning.CritMultiplier : weaponDamage;

    /// <summary>Damage after armor (EntityLivingBase.applyArmorCalculations): times (25 - armor) / 25.</summary>
    public static double AfterArmor(double damage, int armorPoints) =>
        damage * (25 - Math.Clamp(armorPoints, 0, 20)) / 25.0;

    /// <summary>
    /// The motion a velocity packet (S12PacketEntityVelocity) delivers: each axis clamped to
    /// 3.9 and rounded toward zero to a multiple of 1/8000.
    /// </summary>
    public static Vec3d VelocityPacket(Vec3d motion)
    {
        static double Axis(double v) =>
            Math.Truncate(Math.Clamp(v, -Tuning.VelocityPacketClamp, Tuning.VelocityPacketClamp) * Tuning.VelocityPacketScale)
            / Tuning.VelocityPacketScale;
        return new Vec3d(Axis(motion.X), Axis(motion.Y), Axis(motion.Z));
    }

    /// <summary>
    /// Knockback in Minecraft units (blocks per MC tick). <paramref name="motion"/> is the
    /// motion of the server's copy of the victim (<see cref="PlayerMotor.ServerMotion"/>), which
    /// is what 1.8.9 starts from, not the victim's real motion.
    /// <paramref name="baseKnockback"/> is EntityLivingBase.knockBack: halve the current motion,
    /// push 0.4 away from the attacker and 0.4 up (capped at 0.4).
    /// <paramref name="sprintBonus"/> is the sprint-hit push from
    /// EntityPlayer.attackTargetEntityWithCurrentItem: 0.5 along the attacker's facing and 0.1 up.
    /// </summary>
    public static Vec3d Knockback(
        Vec3d motion, Vec3d attackerPosition, Vec3d victimPosition, double attackerYaw,
        bool baseKnockback, bool sprintBonus, Random random)
    {
        if (baseKnockback)
        {
            double dx = attackerPosition.X - victimPosition.X;
            double dz = attackerPosition.Z - victimPosition.Z;
            while (dx * dx + dz * dz < 1.0e-4)
            {
                // Standing in the same spot: Minecraft picks a tiny random direction.
                dx = (random.NextDouble() - random.NextDouble()) * 0.01;
                dz = (random.NextDouble() - random.NextDouble()) * 0.01;
            }
            double length = Math.Sqrt(dx * dx + dz * dz);
            double y = Math.Min(motion.Y / 2 + Tuning.KnockbackVertical, Tuning.KnockbackVertical);
            motion = new Vec3d(
                motion.X / 2 - dx / length * Tuning.KnockbackHorizontal,
                y,
                motion.Z / 2 - dz / length * Tuning.KnockbackHorizontal);
        }
        if (sprintBonus)
        {
            motion += Vec3d.Forward(attackerYaw) * Tuning.SprintKnockbackBonus
                + new Vec3d(0, Tuning.SprintHitVerticalBonus, 0);
        }
        return motion;
    }
}

/// <summary>What a hit did.</summary>
public enum HitResult
{
    /// <summary>No effect: the player is dead, or protected and the hit wasn't stronger.</summary>
    Ignored,
    /// <summary>
    /// Landed during hit protection but was stronger than the last hit: only the extra damage
    /// counts, with no base knockback (a sprint hit still adds its sprint push).
    /// </summary>
    Stronger,
    /// <summary>A full hit: damage, knockback and the red flash.</summary>
    Hurt,
}

/// <summary>
/// Hit protection (EntityLivingBase.attackEntityFrom and hurtResistantTime): for 10 Minecraft
/// ticks after a full hit, a hit only counts if it's stronger than the last one, and then only
/// the extra damage applies.
///
/// A server only handles clicks at its 1/20 s ticks, so whether a hit comes "too soon" is
/// decided on that tick grid: two clicks land 10 ticks apart if the ticks they fall in are 10
/// apart, which in real time is anywhere from 450 to 500 ms. We use the same grid, on the shared
/// host clock, and the moment of the click (not when it arrives), so lag doesn't change it.
/// </summary>
public sealed class HitProtection
{
    private long? _lastFullHitTick;

    /// <summary>Raw damage of the last hit that counted.</summary>
    public double LastDamage { get; private set; }

    /// <summary>The Minecraft tick (on the 20 Hz host-time grid) a moment falls in.</summary>
    public static long TickAt(double hostTime) => (long)Math.Floor(hostTime * Tuning.McTickRate);

    /// <summary>
    /// Decides a hit with <paramref name="rawDamage"/> made in Minecraft tick <paramref name="tick"/>
    /// and records it. Returns what it did and how much damage (before armor) to apply.
    /// </summary>
    public (HitResult Result, double Damage) Hit(double rawDamage, long tick)
    {
        if (rawDamage <= 0)
        {
            return (HitResult.Ignored, 0);
        }
        if (_lastFullHitTick is long last && tick - last < Tuning.HurtImmunityThresholdMcTicks)
        {
            if (rawDamage <= LastDamage)
            {
                return (HitResult.Ignored, 0);
            }
            double extra = rawDamage - LastDamage;
            LastDamage = rawDamage;
            return (HitResult.Stronger, extra);
        }
        _lastFullHitTick = tick;
        LastDamage = rawDamage;
        return (HitResult.Hurt, rawDamage);
    }

    public void Reset()
    {
        _lastFullHitTick = null;
        LastDamage = 0;
    }
}

/// <summary>
/// A player's health and hit protection. Owned by whoever runs that player (their own game, or
/// the host for bots).
/// </summary>
public sealed class Vitality
{
    private static readonly int N = Tuning.SubticksPerMcTick;

    private readonly HitProtection _protection = new();

    public double Health { get; private set; } = Tuning.MaxHealth;
    public bool IsDead => Health <= 0;
    /// <summary>Subticks of the red hurt flash left.</summary>
    public int HurtFlash { get; private set; }
    /// <summary>1 right after a hit, falling to 0 as the flash ends.</summary>
    public float HurtFlashFraction => HurtFlash / (float)(Tuning.HurtFlashMcTicks * N);

    /// <summary>A hit made in Minecraft tick <paramref name="tick"/> (see <see cref="HitProtection.TickAt"/>).</summary>
    public HitResult TakeHit(double rawDamage, int armorPoints, long tick)
    {
        if (IsDead)
        {
            return HitResult.Ignored;
        }
        (HitResult result, double damage) = _protection.Hit(rawDamage, tick);
        if (result == HitResult.Ignored)
        {
            return result;
        }
        Lose(CombatRules.AfterArmor(damage, armorPoints));
        if (result == HitResult.Hurt)
        {
            HurtFlash = Tuning.HurtFlashMcTicks * N;
        }
        return result;
    }

    /// <summary>Advances one subtick.</summary>
    public void Tick()
    {
        if (HurtFlash > 0)
        {
            HurtFlash--;
        }
    }

    /// <summary>Full health, no protection (respawning).</summary>
    public void Reset()
    {
        Health = Tuning.MaxHealth;
        _protection.Reset();
        HurtFlash = 0;
    }

    /// <summary>Sets health directly, for showing other players' health.</summary>
    public void Show(double health) => Health = Math.Clamp(health, 0, Tuning.MaxHealth);

    private void Lose(double amount) => Health = Math.Max(Health - amount, 0);
}
