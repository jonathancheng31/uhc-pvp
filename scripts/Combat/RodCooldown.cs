using UhcPvp.Core;

namespace UhcPvp.Combat;

/// <summary>
/// The fishing rod's cooldown (our own rule; 1.8 has none): after a throw, right-clicks do
/// nothing for <see cref="Tuning.RodCooldownMcTicks"/> Minecraft ticks, and the old bobber stays
/// out. Counted in our 1/60 s ticks, so it doesn't depend on the clock or the frame rate.
/// </summary>
public sealed class RodCooldown
{
    /// <summary>The cooldown's length in our ticks.</summary>
    public static readonly int Ticks = Tuning.RodCooldownMcTicks * Tuning.SubticksPerMcTick;

    private int _left;

    public bool Ready => _left == 0;

    /// <summary>How much of the cooldown is still to go: 1 right after a throw, 0 when ready.</summary>
    public double FractionLeft => (double)_left / Ticks;

    /// <summary>A right-click: whether it throws now (which starts the cooldown).</summary>
    public bool TryThrow()
    {
        if (_left > 0)
        {
            return false;
        }
        _left = Ticks;
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
}
