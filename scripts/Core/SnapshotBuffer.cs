using System;
using System.Collections.Generic;

namespace UhcPvp.Core;

/// <summary>
/// Keeps recent timestamped states of one other player and blends between them, so they
/// move smoothly even though updates arrive unevenly.
///
/// It also decides how far in the past to show that player: just enough to cover how late
/// their updates usually arrive, and no more. Showing them any older is what causes "ghosting".
///
/// A respawn is shown on the same delay as everything else: whoever sees the player in the past
/// sees their body until their view reaches the respawn, then the player at their spawn, with
/// no sliding between. Hits and catches follow the same view (<see cref="Sighting"/>).
/// </summary>
public sealed class SnapshotBuffer
{
    public const int MaxStates = 120;
    /// <summary>How strongly the delay estimate follows new measurements (0..1).</summary>
    public const double AgeSmoothing = 0.1;
    /// <summary>How quickly the render delay moves toward its target (0..1 per update).</summary>
    public const double DelaySmoothing = 0.05;

    private readonly List<(double Time, PlayerState State)> _states = new();
    private double _ageAvg;
    private double _ageDev;
    private bool _hasAge;
    private double _killedAt = double.NegativeInfinity;

    /// <summary>
    /// A state showing a player alive this long after the hit that killed them is from their
    /// next life: half the shortest respawn, far longer than the news of a death takes to arrive.
    /// </summary>
    private static readonly double SurelyRespawned = Math.Min(Tuning.RespawnDelay, Tuning.BotRespawnDelay) / 2;

    /// <summary>How far behind the host clock this player is drawn, in seconds.</summary>
    public double RenderDelay { get; private set; } = Tuning.InterpMinDelay;

    public int Count => _states.Count;
    public bool IsEmpty => _states.Count == 0;
    public double LatestTime => IsEmpty ? 0.0 : _states[^1].Time;
    public PlayerState LatestState => IsEmpty ? default : _states[^1].State;
    /// <summary>
    /// Host time of the state that last brought this player back (the first alive one after a
    /// dead one), or negative infinity if they haven't respawned.
    /// </summary>
    public double RespawnedAt { get; private set; } = double.NegativeInfinity;

    /// <summary>
    /// Whether they're dead as far as their states and the news of their death go: from their
    /// latest state, or the hit that killed them (which can arrive before the states that show
    /// it), until a state shows them back.
    /// </summary>
    public bool KnownDead => LatestState.Has(PlayerFlags.Dead) || _killedAt > RespawnedAt;

    /// <summary>A hit at host time <paramref name="time"/> killed this player.</summary>
    public void NoteKilled(double time) => _killedAt = Math.Max(_killedAt, time);

    /// <summary>
    /// Whether a view of this player at host time <paramref name="time"/> shows their current
    /// life: from their latest respawn on. Earlier, it shows their old body.
    /// </summary>
    public bool ShowsCurrentLife(double time) => time >= RespawnedAt;

    /// <summary>Adds a state stamped with host time. Older or repeated states are ignored.</summary>
    public bool Push(double time, PlayerState state)
    {
        if (!IsEmpty && time <= LatestTime)
        {
            return false;
        }
        if (!IsEmpty && LatestState.Has(PlayerFlags.Dead) && !state.Has(PlayerFlags.Dead))
        {
            RespawnedAt = time;
        }
        else if (!state.Has(PlayerFlags.Dead) && _killedAt > RespawnedAt && time > _killedAt + SurelyRespawned)
        {
            // Alive long after the hit that killed them, with every state showing them dead lost
            // on the way: they've respawned, or nobody here could hit them again.
            RespawnedAt = time;
        }
        _states.Add((time, state));
        if (_states.Count > MaxStates)
        {
            _states.RemoveAt(0);
        }
        return true;
    }

    /// <summary>Records how old a state already was when it arrived, and updates the render delay.</summary>
    public void NoteArrivalAge(double age)
    {
        if (!_hasAge)
        {
            _ageAvg = age;
            _ageDev = 0.0;
            _hasAge = true;
            RenderDelay = TargetDelay();
            return;
        }
        _ageDev += (Math.Abs(age - _ageAvg) - _ageDev) * AgeSmoothing;
        _ageAvg += (age - _ageAvg) * AgeSmoothing;
        RenderDelay += (TargetDelay() - RenderDelay) * DelaySmoothing;
    }

    /// <summary>Usual arrival age, plus a margin for jitter, plus one send interval.</summary>
    public double TargetDelay()
    {
        double target = _ageAvg + 2.0 * _ageDev + 1.0 / Tuning.StateSendRate;
        return Math.Max(target, Tuning.InterpMinDelay);
    }

    /// <summary>
    /// The blended state at a host-clock time. Holds the first or last state when the time
    /// is outside the stored range, and the body until the moment of a respawn.
    /// </summary>
    public PlayerState Sample(double time)
    {
        int n = _states.Count;
        if (n == 0)
        {
            return default;
        }
        if (time <= _states[0].Time)
        {
            return _states[0].State;
        }
        if (time >= _states[n - 1].Time)
        {
            return _states[n - 1].State;
        }
        int lo = 0;
        int hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (_states[mid].Time <= time)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }
        var (t0, a) = _states[lo];
        var (t1, b) = _states[hi];
        if (a.Has(PlayerFlags.Dead) && !b.Has(PlayerFlags.Dead))
        {
            // Respawned at the next state: they jump to their spawn then, not slide there.
            return a;
        }
        return PlayerState.Blend(a, b, (float)((time - t0) / (t1 - t0)));
    }
}
