using UhcPvp.Core;

namespace UhcPvp.Combat;

/// <summary>
/// Our "wasted clicks" twist (a match rule, not 1.8.9), to take power away from clicking speed:
/// a click that does nothing blocks your next clicks for a moment (no swing, no hit), so
/// spamming makes your first real hit come later and one well-timed click beats it. Counted in
/// our 1/60 s ticks on the clicker's own game. Clicks it blocks don't restart it.
/// </summary>
public sealed class ClickLockout
{
    private int _left;
    private int _length = 1;

    /// <summary>Whether a click now is blocked.</summary>
    public bool Blocks => _left > 0;

    /// <summary>How much of the lockout is still to go: 1 right after a wasted click, 0 when clicks work.</summary>
    public double FractionLeft => (double)_left / _length;

    /// <summary>A click was wasted: block clicks for <paramref name="seconds"/> (0 does nothing, as in 1.8.9).</summary>
    public void Wasted(double seconds)
    {
        int ticks = TicksFor(seconds);
        if (ticks > 0)
        {
            _left = ticks;
            _length = ticks;
        }
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

    public static int TicksFor(double seconds) => (int)System.Math.Round(seconds * Tuning.SimRate);

    /// <summary>
    /// Whether a click did nothing: it hit nothing, a block or a teammate, or a player who was
    /// still protected and the hit wasn't stronger (<paramref name="result"/> is null when no
    /// enemy was hit). A hit that counts, even a stronger one inside protection, isn't wasted.
    /// </summary>
    public static bool IsWasted(HitResult? result) => result is null or HitResult.Ignored;
}
