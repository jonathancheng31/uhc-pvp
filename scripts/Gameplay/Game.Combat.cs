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
/// 1. The attacker's game decides a hit from exactly what was on its screen (TryAttack, or a
///    bobber catching someone in Game.Rod.cs).
/// 2. The hit goes to the game that owns the victim (their own, or the host for bots), which
///    checks the lag cap and hit protection and applies damage and knockback (ResolveAttack).
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
    /// <summary>
    /// Our best guess at the hit protection of players another game runs, kept from the hits we
    /// make and the hits we hear about. It predicts whether our hit will count before the
    /// victim's game answers: exact in a 1v1, since protection is counted from click times.
    /// </summary>
    private readonly Dictionary<int, HitProtection> _protectionGuess = new();

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
        bool swung = attacker.PlaySwing();
        SendToAll(new Swing(attacker.PeerId));

        double now = Net.HostTime;
        _aimTargets.Clear();
        foreach (Player p in _players.Values)
        {
            if (p != attacker && SeenBy(attacker, p, now) is { Hittable: true } seen)
            {
                _aimTargets.Add(new AimTarget(p.PeerId, seen.Feet));
            }
        }
        AimResult aim = HitScan.Aim(attacker.Eye, HitScan.LookVector(attacker.Yaw, attacker.Pitch), _aimTargets, World);
        if (swung)
        {
            PlaySwingSound(attacker, connected: aim.Kind == AimKind.Player);
        }
        if (aim.Kind == AimKind.Miss)
        {
            attacker.StartMissCooldown();
            WasteClick(attacker, null);
            return;
        }
        if (aim.Kind != AimKind.Player || !_players.TryGetValue(aim.TargetId, out Player? target)
            || (!Tuning.FriendlyFire && target.Team == attacker.Team))
        {
            WasteClick(attacker, null);
            return;
        }

        bool critical = attacker.Motor.CanCrit;
        bool sprintKnockback = attacker.Motor.ServerSprinting;
        var flags = (critical ? AttackFlags.Critical : 0) | (sprintKnockback ? AttackFlags.SprintKnockback : 0);
        var attack = new Attack(
            attacker.PeerId, target.PeerId, now, target.ViewTime(now),
            attacker.Feet.ToGodot(), attacker.Yaw, SeenBy(attacker, target, now).Feet.ToGodot(), flags);
        if (attacker.IsLocal)
        {
            LastReach = aim.Distance;
            if (critical)
            {
                // Your own game shows crit sparks on every critical click, like 1.8's client.
                Effects.CritBurst(this, Chest(target));
            }
        }

        HitResult result;
        if (target.IsOwned)
        {
            result = ResolveAttack(attack);
        }
        else
        {
            double raw = CombatRules.RawDamage(Tuning.SwordDamage, critical);
            result = GuessProtection(target.PeerId).Hit(raw, HitProtection.TickAt(now)).Result;
            if (attacker.IsLocal && result == HitResult.Hurt)
            {
                PredictHurt(target, critical, rod: false);
            }
            DeliverAttack(attack);
        }
        WasteClick(attacker, result);
        bool counted = result != HitResult.Ignored;
        attacker.Motor.OnHitLanded(counted);
        attacker.Brain?.OnAttack(target.PeerId, result, sprintKnockback);
    }

    /// <summary>
    /// The wasted-clicks twist: a click that did nothing (<see cref="ClickLockout.IsWasted"/>)
    /// blocks the attacker's clicks for the match's delay (none in 1.8.9). Decided on the
    /// attacker's game, from what it sees and its guess of the target's hit protection.
    /// </summary>
    private static void WasteClick(Player attacker, HitResult? result)
    {
        if (ClickLockout.IsWasted(result))
        {
            attacker.ClickLockout.Wasted(Session.Rules.WastedClickDelay);
        }
    }

    /// <summary>
    /// Where a player this game runs sees <paramref name="target"/> at host time
    /// <paramref name="now"/>, which is where its clicks and its bobber find them, and whether
    /// they can be hit there (alive as seen, and not known to have died since): you, as they
    /// were last drawn on your screen; a bot, the same view (<see cref="Player.SeenAt"/>), you
    /// included. Both are the head start in the past.
    /// </summary>
    private static Sighting SeenBy(Player viewer, Player target, double now) =>
        viewer.IsLocal ? target.Shown : target.SeenAt(now);

    private HitProtection GuessProtection(int playerId)
    {
        if (!_protectionGuess.TryGetValue(playerId, out HitProtection? guess))
        {
            guess = new HitProtection();
            _protectionGuess[playerId] = guess;
        }
        return guess;
    }

    /// <summary>
    /// Hitting someone whose game is elsewhere: show the flash and sound right away instead of
    /// waiting a round trip.
    /// </summary>
    private void PredictHurt(Player target, bool critical, bool rod)
    {
        _predictedAt[target.PeerId] = Network.Instance.Now;
        target.ShowHurt(new Hurt(target.PeerId, LocalPlayer.PeerId, (float)target.Health, HurtFlags.Fresh, Net.HostTime));
        PlayBlowSounds(target, byMe: true, fresh: true, killed: false, critical, rod, Combo + 1);
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
    private HitResult ResolveAttack(Attack attack)
    {
        if (!_players.TryGetValue(attack.TargetId, out Player? victim) || victim.Motor is null || victim.IsDead
            || !double.IsFinite(attack.AttackTime) || !double.IsFinite(attack.ViewTime))
        {
            return HitResult.Ignored;
        }
        Player? attacker = _players.GetValueOrDefault(attack.AttackerId);
        if (attacker is not null && !Tuning.FriendlyFire && attacker.Team == victim.Team)
        {
            return HitResult.Ignored;
        }
        // A view of us from before our latest respawn shows our old body, not us (with a tick's
        // allowance, as for the lag cap below).
        if (!victim.History.ShowsCurrentLife(attack.ViewTime + 1.0 / Tuning.SimRate))
        {
            GD.Print($"[combat] ignored a hit on {victim.PlayerName}: attacker saw them before they respawned");
            return HitResult.Ignored;
        }
        // The lag cap: the attacker's view of us must be no older than the head start (or the
        // lag cap, if that's more). Older is the attacker's connection falling behind: a ghost hit.
        double staleness = attack.AttackTime - attack.ViewTime;
        if (staleness > HeadStart.MaxViewAge(Session.Rules.HeadStart) + 1.0 / Tuning.SimRate)
        {
            GD.Print($"[combat] ignored a hit on {victim.PlayerName}: attacker's view was {staleness * 1000:0} ms old");
            return HitResult.Ignored;
        }
        Vec3d attackerFeet = Vec3d.From(attack.AttackerFeet);
        Vec3d seenFeet = Vec3d.From(attack.TargetFeet);
        // Knockback pushes away from the attacker as the two really stood when they clicked, as a
        // 1.8.9 server's copies of them do, not from where the attacker saw us (the head start ago).
        Vec3d victimFeet = victim.History.IsEmpty ? victim.Feet : Vec3d.From(victim.History.Sample(attack.AttackTime).Position);
        Vec3d attackerEye = attackerFeet + new Vec3d(0, Tuning.EyeHeight, 0);
        bool rod = (attack.Flags & AttackFlags.Rod) != 0;
        bool tooFar = rod
            ? (seenFeet - attackerFeet).Length > Tuning.RodLineLength + ReachTolerance
            : HitScan.Hitbox(seenFeet).DistanceTo(attackerEye) > Tuning.Reach + ReachTolerance;
        if (tooFar)
        {
            GD.Print($"[combat] ignored a hit on {victim.PlayerName}: too far");
            return HitResult.Ignored;
        }

        bool critical = !rod && (attack.Flags & AttackFlags.Critical) != 0;
        bool sprintKnockback = !rod && (attack.Flags & AttackFlags.SprintKnockback) != 0;
        double comboKnockback = Session.Rules.ComboKnockback;
        HitResult result = victim.Vitality.TakeHit(
            RawDamage(rod, critical), Tuning.KitArmorPoints, HitProtection.TickAt(attack.AttackTime));
        if (result == HitResult.Ignored)
        {
            return result;
        }
        if (result == HitResult.Hurt)
        {
            // Knockback starts from the server's copy of the victim, as in 1.8.9 (see
            // PlayerMotor.ServerMotion), and lands at the victim's next Minecraft tick like a
            // velocity packet. The server keeps its own copy as it was.
            victim.Motor.Knock(copy => CombatRules.VelocityPacket(CombatRules.Knockback(
                copy, attackerFeet, victimFeet, attack.AttackerYaw, baseKnockback: true, sprintBonus: sprintKnockback, _random,
                comboKnockback)));
        }
        else if (sprintKnockback)
        {
            // A stronger hit sends nothing: the victim doesn't move, but a sprint push stays on
            // the server's copy and adds to the next knockback.
            victim.Motor.PushServerCopy(
                CombatRules.Knockback(Vec3d.Zero, attackerFeet, victimFeet, attack.AttackerYaw, false, true, _random, comboKnockback));
        }
        victim.OnDamaged();

        var flags = (result == HitResult.Hurt ? HurtFlags.Fresh : 0)
            | (critical ? HurtFlags.Critical : 0)
            | (victim.IsDead ? HurtFlags.Killed : 0)
            | (rod ? HurtFlags.Rod : 0);
        var hurt = new Hurt(victim.PeerId, attack.AttackerId, (float)victim.Vitality.Health, flags, attack.AttackTime);
        ApplyHurt(hurt);
        SendToAll(hurt);
        return result;
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
        bool critical = (hurt.Flags & HurtFlags.Critical) != 0;
        bool rod = (hurt.Flags & HurtFlags.Rod) != 0;
        bool byMe = hurt.AttackerId == LocalPlayer.PeerId;
        bool predicted = byMe
            && _predictedAt.Remove(hurt.VictimId, out double predictedAt) && now - predictedAt < PredictionWindow;

        // Keep our guess of this player's hit protection in step with what really happened.
        if (killed)
        {
            _protectionGuess.Remove(hurt.VictimId);
        }
        else if (double.IsFinite(hurt.AttackTime))
        {
            GuessProtection(hurt.VictimId).Hit(RawDamage(rod, critical), HitProtection.TickAt(hurt.AttackTime));
        }

        victim.ShowHurt(predicted ? hurt with { Flags = hurt.Flags & ~HurtFlags.Fresh } : hurt);
        Vector3 chest = Chest(victim);
        if (!predicted)
        {
            PlayBlowSounds(victim, byMe, fresh, killed, critical, rod, Combo + 1);
        }
        else if (killed)
        {
            PlayHurtSound(victim, killed);
        }
        if (critical && !byMe)
        {
            Effects.CritBurst(this, chest);
        }

        // Rod hits are counted on their own; the combo readout is for sword hits.
        if (byMe && rod)
        {
            RodHitsLanded++;
        }
        else if (byMe)
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
        if (hurt.VictimId == LocalPlayer.PeerId && fresh && !rod)
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
        if (_players.TryGetValue(swing.PlayerId, out Player? player) && !player.IsOwned && player.PlaySwing())
        {
            PlaySwingSound(player, connected: null);
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

    /// <summary>A hit's damage before armor: the sword's, or none for a rod.</summary>
    private static double RawDamage(bool rod, bool critical) =>
        rod ? Tuning.RodDamage : CombatRules.RawDamage(Tuning.SwordDamage, critical);
}
