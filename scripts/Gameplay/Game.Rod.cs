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
/// The fishing rod (docs/rod.md). Right-click throws it; right-clicking again makes the old
/// bobber vanish and throws a new one. Like a sword hit, the thrower's game decides whom its
/// bobber catches, from what it shows, and the victim's game applies the hit (ResolveAttack):
/// no damage, knockback away from the thrower, and it counts for hit protection.
///
/// Every game flies each bobber from its cast (<see cref="Bobber"/>). Someone else's bobber is
/// shown on the same delay as they are, so it leaves their hand and catches people as drawn.
/// </summary>
public partial class Game
{
    private readonly Dictionary<int, BobberView> _bobbers = new();
    private readonly List<RodTarget> _rodTargets = new();

    /// <summary>Rod hits by the local player.</summary>
    public int RodHitsLanded { get; private set; }

    /// <summary>A right-click by a player this game runs (you or a bot).</summary>
    public void ThrowRod(Player thrower)
    {
        if (!thrower.IsOwned || thrower.IsDead)
        {
            return;
        }
        double now = Net.HostTime;
        // Rounded to what the message carries, so every game flies exactly the same path.
        Vector3 origin = Bobber.CastOrigin(thrower.Eye, thrower.Yaw).ToGodot();
        Vector3 velocity = Bobber.CastVelocity(thrower.Yaw, thrower.Pitch, _random).ToGodot();
        StartBobber(thrower, now, origin, velocity);
        SendToAll(new RodCast(thrower.PeerId, now, origin, velocity));
    }

    private void StartBobber(Player thrower, double time, Vector3 origin, Vector3 velocity)
    {
        RemoveBobber(thrower.PeerId);
        var bobber = new Bobber(World, thrower.PeerId, time, Vec3d.From(origin), Vec3d.From(velocity));
        var view = new BobberView(this, bobber) { Name = $"Bobber_{thrower.PeerId}", CheckedUntil = time };
        AddChild(view);
        _bobbers[thrower.PeerId] = view;
    }

    private void RemoveBobber(int throwerId)
    {
        if (_bobbers.Remove(throwerId, out BobberView? view))
        {
            view.QueueFree();
        }
    }

    /// <summary>Each tick: the bobbers of players this game runs look for someone to catch.</summary>
    private void StepRods(double now)
    {
        foreach (BobberView view in _bobbers.Values)
        {
            Bobber bobber = view.Bobber;
            if (now <= view.CheckedUntil || !_players.TryGetValue(bobber.ThrowerId, out Player? thrower) || !thrower.IsOwned)
            {
                continue;
            }
            if (bobber.HookedId is not null)
            {
                // Hanging on someone: nothing to catch, but its clock keeps running for when it drops.
                view.CheckedUntil = now;
                continue;
            }
            double from = view.CheckedUntil;
            Vec3d a = bobber.PositionAt(from);
            Vec3d b = bobber.PositionAt(now);
            view.CheckedUntil = now;
            _rodTargets.Clear();
            foreach (Player p in _players.Values)
            {
                if (p != thrower && !p.IsDead && (Tuning.FriendlyFire || p.Team != thrower.Team))
                {
                    _rodTargets.Add(new RodTarget(p.PeerId, p.ShownFeet));
                }
            }
            if (Bobber.FirstHit(a, b, _rodTargets) is { } hit && _players.TryGetValue(hit.Id, out Player? target))
            {
                double time = from + (now - from) * hit.Fraction;
                if (RodHit(thrower, target, time))
                {
                    bobber.Hook(target.PeerId);
                    SendToAll(new RodHook(thrower.PeerId, target.PeerId, time));
                }
            }
        }
    }

    /// <summary>
    /// The thrower's side of a bobber touching someone. Returns whether it counts: a player
    /// still in hit protection isn't caught, and the bobber flies on through (as on 1.8 servers).
    /// </summary>
    private bool RodHit(Player thrower, Player target, double time)
    {
        var attack = new Attack(
            thrower.PeerId, target.PeerId, time, target.ShownTime(time),
            thrower.Feet.ToGodot(), thrower.Yaw, target.ShownFeet.ToGodot(), AttackFlags.Rod);
        if (target.IsOwned)
        {
            return ResolveAttack(attack) != HitResult.Ignored;
        }
        HitResult guess = GuessProtection(target.PeerId).Hit(Tuning.RodDamage, HitProtection.TickAt(time)).Result;
        if (guess == HitResult.Ignored)
        {
            return false;
        }
        if (thrower.IsLocal)
        {
            PredictHurt(target);
        }
        DeliverAttack(attack);
        return true;
    }

    /// <summary>
    /// Every frame, after the players have moved: places a bobber and its line, or removes it
    /// (thrower dead or gone, line stretched too far, fell out of the world). Called by the view.
    /// </summary>
    internal void UpdateBobber(BobberView view)
    {
        Bobber bobber = view.Bobber;
        if (!_players.TryGetValue(bobber.ThrowerId, out Player? thrower) || thrower.IsDead)
        {
            RemoveBobber(bobber.ThrowerId);
            return;
        }
        // Our own bobbers only go as far as they've been checked for hits.
        double time = thrower.IsOwned ? view.CheckedUntil : thrower.ShownTime(Net.HostTime);
        if (view.PendingHook is (int victimId, double hookTime) && time >= hookTime)
        {
            bobber.Hook(victimId);
            view.PendingHook = null;
        }
        Vec3d position;
        if (bobber.HookedId is int hookedId)
        {
            if (_players.TryGetValue(hookedId, out Player? victim) && !victim.IsDead)
            {
                position = Bobber.HookPoint(victim.ShownFeet);
            }
            else
            {
                // Whoever it hung on is gone: it drops from there.
                position = view.Shown;
                bobber.Release(time, position);
            }
        }
        else
        {
            position = bobber.PositionAt(time);
        }
        if ((position - thrower.ShownFeet).Length > Tuning.RodLineLength || position.Y < Tuning.VoidY)
        {
            RemoveBobber(bobber.ThrowerId);
            return;
        }
        if (time < bobber.CastTime)
        {
            return; // someone else's cast that their shown self hasn't made yet
        }
        if (!view.Appeared)
        {
            thrower.PlayCast();
            Effects.PlayCast(this, bobber.Origin.ToGodot());
        }
        view.Show(position, thrower.RodTip, onViewer: bobber.HookedId == LocalPlayer.PeerId && LocalPlayer.Camera is not null);
    }

    private void OnRodCastMessage(int from, RodCast cast)
    {
        if (Net.IsHost)
        {
            if (Session.OwnerOf(cast.PlayerId) != from)
            {
                return;
            }
            Net.Broadcast(cast, reliable: true, exceptId: from);
        }
        if (!_players.TryGetValue(cast.PlayerId, out Player? thrower) || thrower.IsOwned
            || !IsFinite(cast.Origin) || !IsFinite(cast.Velocity) || !double.IsFinite(cast.Time)
            || cast.Velocity.Length() > Tuning.RodCastSpeed * 2)
        {
            return;
        }
        double now = Net.HostTime;
        // A cast time far from now means the sender's clock is still settling.
        double time = Math.Abs(cast.Time - now) > 2.0 ? now : cast.Time;
        StartBobber(thrower, time, cast.Origin, cast.Velocity);
    }

    private void OnRodHookMessage(int from, RodHook hook)
    {
        if (Net.IsHost)
        {
            if (Session.OwnerOf(hook.PlayerId) != from)
            {
                return;
            }
            Net.Broadcast(hook, reliable: true, exceptId: from);
        }
        if (_bobbers.TryGetValue(hook.PlayerId, out BobberView? view) && view.Bobber.HookedId is null
            && double.IsFinite(hook.Time) && hook.Time >= view.Bobber.CastTime && _players.ContainsKey(hook.VictimId)
            && _players.TryGetValue(hook.PlayerId, out Player? thrower) && !thrower.IsOwned)
        {
            view.PendingHook = (hook.VictimId, hook.Time);
        }
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
