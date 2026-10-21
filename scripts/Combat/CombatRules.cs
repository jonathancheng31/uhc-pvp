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
    /// The victim's new motion in Minecraft units (blocks per MC tick) after a hit.
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
/// A player's health and hit protection, following EntityLivingBase.attackEntityFrom. Owned by
/// whoever runs that player (their own game, or the host for bots).
/// </summary>
public sealed class Vitality
{
    private static readonly int N = Tuning.SubticksPerMcTick;

    private double _lastDamage;
    private int _protection;

    public double Health { get; private set; } = Tuning.MaxHealth;
    public bool IsDead => Health <= 0;
    /// <summary>Subticks of the red hurt flash left.</summary>
    public int HurtFlash { get; private set; }
    /// <summary>1 right after a hit, falling to 0 as the flash ends.</summary>
    public float HurtFlashFraction => HurtFlash / (float)(Tuning.HurtFlashMcTicks * N);

    public HitResult TakeHit(double rawDamage, int armorPoints)
    {
        if (IsDead || rawDamage <= 0)
        {
            return HitResult.Ignored;
        }
        if (_protection > Tuning.HurtImmunityThresholdMcTicks * N)
        {
            if (rawDamage <= _lastDamage)
            {
                return HitResult.Ignored;
            }
            Lose(CombatRules.AfterArmor(rawDamage - _lastDamage, armorPoints));
            _lastDamage = rawDamage;
            return HitResult.Stronger;
        }
        _lastDamage = rawDamage;
        _protection = Tuning.HurtResistantMcTicks * N;
        Lose(CombatRules.AfterArmor(rawDamage, armorPoints));
        HurtFlash = Tuning.HurtFlashMcTicks * N;
        return HitResult.Hurt;
    }

    /// <summary>Advances one subtick.</summary>
    public void Tick()
    {
        if (_protection > 0)
        {
            _protection--;
        }
        if (HurtFlash > 0)
        {
            HurtFlash--;
        }
    }

    /// <summary>Full health, no protection (respawning).</summary>
    public void Reset()
    {
        Health = Tuning.MaxHealth;
        _lastDamage = 0;
        _protection = 0;
        HurtFlash = 0;
    }

    /// <summary>Sets health directly, for showing other players' health.</summary>
    public void Show(double health) => Health = Math.Clamp(health, 0, Tuning.MaxHealth);

    private void Lose(double amount) => Health = Math.Max(Health - amount, 0);
}
