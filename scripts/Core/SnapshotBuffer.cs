using System;
using System.Collections.Generic;

namespace UhcPvp.Core;

/// <summary>
/// Keeps recent timestamped states of one other player and blends between them, so they
/// move smoothly even though updates arrive unevenly.
///
/// It also decides how far in the past to show that player: just enough to cover how late
/// their updates usually arrive, and no more. Showing them any older is what causes "ghosting".
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

    /// <summary>How far behind the host clock this player is drawn, in seconds.</summary>
    public double RenderDelay { get; private set; } = Tuning.InterpMinDelay;

    public int Count => _states.Count;
    public bool IsEmpty => _states.Count == 0;
    public double LatestTime => IsEmpty ? 0.0 : _states[^1].Time;
    public PlayerState LatestState => IsEmpty ? default : _states[^1].State;

    /// <summary>Forgets stored states, so the next one shows instantly (e.g. after a respawn teleport).</summary>
    public void Clear() => _states.Clear();

    /// <summary>Adds a state stamped with host time. Older or repeated states are ignored.</summary>
    public bool Push(double time, PlayerState state)
    {
        if (!IsEmpty && time <= LatestTime)
        {
            return false;
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
    /// is outside the stored range.
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
        return PlayerState.Blend(a, b, (float)((time - t0) / (t1 - t0)));
    }
}
