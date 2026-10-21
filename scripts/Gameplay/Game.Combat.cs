using System;
using System.Collections.Generic;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.Net;

namespace UhcPvp.Gameplay;

/// <summary>
/// Combat, following the plan in docs/netcode.md:
/// 1. The attacker's game decides a hit from exactly what was on its screen (TryAttack).
/// 2. The hit goes to the game that owns the victim (their own, or the host for bots), which
///    checks the lag cap and applies damage and knockback to the victim's real motion
///    (ResolveAttack).
/// 3. Everyone is told what happened and plays the hurt effects (ApplyHurt).
/// </summary>
public partial class Game
{
    /// <summary>Extra reach allowed when the victim double-checks a hit, for rounding and jitter.</summary>
    private const double ReachTolerance = 0.5;
    /// <summary>A predicted hurt flash counts as confirmed if the real hit arrives within this many seconds.</summary>
    private const double PredictionWindow = 0.6;

    private readonly Random _random = new();
    private readonly List<AimTarget> _aimTargets = new();
    private readonly Dictionary<int, double> _predictedAt = new();
    private readonly Dictionary<int, double> _lastFreshHurtAt = new();

    /// <summary>Hits in a row by the local player without being hit back.</summary>
    public int Combo { get; private set; }
    /// <summary>Distance of the local player's last hit, eye to hitbox, in blocks.</summary>
    public double LastReach { get; private set; }
    /// <summary>Local time (seconds) of the local player's last landed hit.</summary>
    public double LastHitAt { get; private set; } = double.NegativeInfinity;
    public int HitsLanded { get; private set; }
    public int HitsTaken { get; private set; }
    public int Kills { get; private set; }

    /// <summary>
    /// A click by a player this game runs (you or a bot). Swings, aims like Minecraft and, if a
    /// player is in the crosshair, sends the hit to whoever owns them.
    /// </summary>
    public void TryAttack(Player attacker)
    {
        if (!attacker.CanAttack || attacker.Motor is null)
        {
            return;
        }
        attacker.PlaySwing();
        SendToAll(new Swing(attacker.PeerId));

        _aimTargets.Clear();
        foreach (Player p in _players.Values)
        {
            if (p != attacker && !p.IsDead)
            {
                _aimTargets.Add(new AimTarget(p.PeerId, p.ShownFeet));
            }
        }
        AimResult aim = HitScan.Aim(attacker.Eye, HitScan.LookVector(attacker.Yaw, attacker.Pitch), _aimTargets, World);
        if (aim.Kind == AimKind.Miss)
        {
            attacker.StartMissCooldown();
            return;
        }
        if (aim.Kind != AimKind.Player || !_players.TryGetValue(aim.TargetId, out Player? target))
        {
            return;
        }
        if (!Tuning.FriendlyFire && target.Team == attacker.Team)
        {
            return;
        }

        bool critical = attacker.Motor.CanCrit;
        bool sprintKnockback = attacker.Motor.OnHitLanded();
        if (sprintKnockback)
        {
            attacker.Brain?.OnSprintHitLanded();
        }
        double now = Net.HostTime;
        var flags = (critical ? AttackFlags.Critical : 0) | (sprintKnockback ? AttackFlags.SprintKnockback : 0);
        var attack = new Attack(
            attacker.PeerId, target.PeerId, now, target.ShownTime(now),
            attacker.Feet.ToGodot(), attacker.Yaw, target.ShownFeet.ToGodot(), flags);
        if (attacker.IsLocal)
        {
            LastReach = aim.Distance;
            if (!target.IsOwned)
            {
                PredictHurt(target);
            }
        }
        DeliverAttack(attack);
    }

    /// <summary>
    /// Hitting someone whose game is elsewhere: show the flash and sound right away instead of
    /// waiting a round trip. Skipped if they were just hit (they're probably still protected).
    /// </summary>
    private void PredictHurt(Player target)
    {
        double now = Network.Instance.Now;
        if (now - _lastFreshHurtAt.GetValueOrDefault(target.PeerId, double.NegativeInfinity)
            < Tuning.HurtImmunityThresholdMcTicks / (double)Tuning.McTickRate)
        {
            return;
        }
        _predictedAt[target.PeerId] = now;
        target.ShowHurt(new Hurt(target.PeerId, LocalPlayer.PeerId, (float)target.Health, HurtFlags.Fresh));
        Effects.PlayHurt(this, Chest(target), killed: false);
    }

    private void DeliverAttack(Attack attack)
    {
        int owner = Session.OwnerOf(attack.TargetId);
        if (owner == Net.MyId)
        {
            ResolveAttack(attack);
        }
        else if (Net.IsHost)
        {
            Net.Send(owner, attack, reliable: true);
        }
        else
        {
            Net.SendToHost(attack, reliable: true);
        }
    }

    /// <summary>The victim's side: checks the hit and applies damage and knockback.</summary>
    private void ResolveAttack(Attack attack)
    {
        if (!_players.TryGetValue(attack.TargetId, out Player? victim) || victim.Motor is null || victim.IsDead)
        {
            return;
        }
        Player? attacker = _players.GetValueOrDefault(attack.AttackerId);
        if (attacker is not null && !Tuning.FriendlyFire && attacker.Team == victim.Team)
        {
            return;
        }
        // The lag cap: the attacker's view of us must not be too old (no ghost hits).
        double staleness = attack.AttackTime - attack.ViewTime;
        if (staleness > Tuning.LagCap + 1.0 / Tuning.SimRate)
        {
            GD.Print($"[combat] ignored a hit on {victim.PlayerName}: attacker's view was {staleness * 1000:0} ms old");
            return;
        }
        Vec3d attackerFeet = Vec3d.From(attack.AttackerFeet);
        Vec3d seenFeet = Vec3d.From(attack.TargetFeet);
        Vec3d attackerEye = attackerFeet + new Vec3d(0, Tuning.EyeHeight, 0);
        if (HitScan.Hitbox(seenFeet).DistanceTo(attackerEye) > Tuning.Reach + ReachTolerance)
        {
            GD.Print($"[combat] ignored a hit on {victim.PlayerName}: too far");
            return;
        }

        bool critical = (attack.Flags & AttackFlags.Critical) != 0;
        bool sprintKnockback = (attack.Flags & AttackFlags.SprintKnockback) != 0;
        HitResult result = victim.Vitality.TakeHit(CombatRules.RawDamage(Tuning.SwordDamage, critical), Tuning.KitArmorPoints);
        if (result == HitResult.Ignored)
        {
            return;
        }
        Vec3d motion = CombatRules.Knockback(
            victim.Motor.McMotion, attackerFeet, seenFeet, attack.AttackerYaw,
            baseKnockback: result == HitResult.Hurt, sprintBonus: sprintKnockback, _random);
        victim.Motor.SetMcMotion(motion);
        victim.OnDamaged();

        var flags = (result == HitResult.Hurt ? HurtFlags.Fresh : 0)
            | (critical ? HurtFlags.Critical : 0)
            | (victim.IsDead ? HurtFlags.Killed : 0);
        var hurt = new Hurt(victim.PeerId, attack.AttackerId, (float)victim.Vitality.Health, flags);
        ApplyHurt(hurt);
        SendToAll(hurt);
    }

    /// <summary>Everyone: show a hit (flash, sound, sparks, health, combo, kill feed).</summary>
    private void ApplyHurt(Hurt hurt)
    {
        if (!_players.TryGetValue(hurt.VictimId, out Player? victim))
        {
            return;
        }
        double now = Network.Instance.Now;
        bool fresh = (hurt.Flags & HurtFlags.Fresh) != 0;
        bool killed = (hurt.Flags & HurtFlags.Killed) != 0;
        bool predicted = hurt.AttackerId == LocalPlayer.PeerId
            && _predictedAt.Remove(hurt.VictimId, out double predictedAt) && now - predictedAt < PredictionWindow;

        victim.ShowHurt(predicted ? hurt with { Flags = hurt.Flags & ~HurtFlags.Fresh } : hurt);
        Vector3 chest = Chest(victim);
        if ((fresh && !predicted) || killed)
        {
            Effects.PlayHurt(this, chest, killed);
        }
        if ((hurt.Flags & HurtFlags.Critical) != 0)
        {
            Effects.CritBurst(this, chest);
        }
        if (fresh)
        {
            _lastFreshHurtAt[hurt.VictimId] = now;
        }

        if (hurt.AttackerId == LocalPlayer.PeerId)
        {
            HitsLanded++;
            if (fresh)
            {
                Combo++;
                LastHitAt = now;
            }
            if (killed)
            {
                Kills++;
            }
        }
        if (hurt.VictimId == LocalPlayer.PeerId && fresh)
        {
            HitsTaken++;
            Combo = 0;
        }
        if (killed)
        {
            string killer = _players.TryGetValue(hurt.AttackerId, out Player? a) ? a.PlayerName : "someone";
            Notice?.Invoke($"{victim.PlayerName} was slain by {killer}");
        }
    }

    private void OnAttackMessage(int from, Attack attack)
    {
        if (Net.IsHost)
        {
            // Only a player's own game may attack as them (the host attacks for bots).
            if (Session.OwnerOf(attack.AttackerId) == from)
            {
                DeliverAttack(attack);
            }
        }
        else if (Session.OwnerOf(attack.TargetId) == Net.MyId)
        {
            ResolveAttack(attack);
        }
    }

    private void OnHurtMessage(int from, Hurt hurt)
    {
        if (Net.IsHost)
        {
            if (Session.OwnerOf(hurt.VictimId) != from)
            {
                return;
            }
            Net.Broadcast(hurt, reliable: true, exceptId: from);
        }
        ApplyHurt(hurt);
    }

    private void OnSwingMessage(int from, Swing swing)
    {
        if (Net.IsHost)
        {
            if (Session.OwnerOf(swing.PlayerId) != from)
            {
                return;
            }
            Net.Broadcast(swing, reliable: true, exceptId: from);
        }
        if (_players.TryGetValue(swing.PlayerId, out Player? player) && !player.IsOwned)
        {
            player.PlaySwing();
        }
    }

    /// <summary>Sends to everyone: the host broadcasts, others go through the host.</summary>
    private static void SendToAll(IMessage message)
    {
        if (Network.Instance.IsHost)
        {
            Network.Instance.Broadcast(message, reliable: true);
        }
        else
        {
            Network.Instance.SendToHost(message, reliable: true);
        }
    }

    private static Vector3 Chest(Player player) => player.Position + new Vector3(0, 1.1f, 0);
}
