using System;
using UhcPvp.Core;

namespace UhcPvp.Combat;

/// <summary>
/// The fishing rod's cooldown (a twist, see <see cref="MatchRules.RodCooldown"/>; 1.8 has none):
/// after a throw, right-clicks do nothing for the match's cooldown, and the old bobber stays out.
/// Counted in our 1/60 s ticks, so it doesn't depend on the clock or the frame rate. Each
/// cooldown keeps the length it started with: if the host changes the rule meanwhile, the new
/// length counts from the next throw.
/// </summary>
public sealed class RodCooldown
{
    private int _left;
    /// <summary>The length of the cooldown that's running (or ran last), in our ticks.</summary>
    private int _length = 1;

    public bool Ready => _left == 0;

    /// <summary>How much of the cooldown is still to go: 1 right after a throw, 0 when ready.</summary>
    public double FractionLeft => (double)_left / _length;

    /// <summary>
    /// A right-click: whether it throws now, which starts a cooldown of <paramref name="seconds"/>
    /// (the match's; 0 lets every right-click throw, as in 1.8.9).
    /// </summary>
    public bool TryThrow(double seconds)
    {
        if (_left > 0)
        {
            return false;
        }
        _left = Math.Max(TicksFor(seconds), 0);
        _length = Math.Max(_left, 1);
        return true;
    }

    /// <summary>One of our ticks (1/60 s).</summary>
    public void Tick()
    {
        if (_left > 0)
        {
            _left--;
        }
    }

    public void Reset() => _left = 0;

    /// <summary>A cooldown's length in our ticks: 0.75 s is 45.</summary>
    public static int TicksFor(double seconds) => (int)Math.Round(seconds * Tuning.SimRate);
}
