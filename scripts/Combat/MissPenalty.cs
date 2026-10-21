using UhcPvp.Core;

namespace UhcPvp.Combat;

/// <summary>
/// Minecraft 1.8.9's miss penalty (Minecraft.leftClickCounter). Clicking at nothing blocks
/// clicks for 10 ticks, but the client clears the penalty at the end of every tick the attack
/// button is up. So it only eats clicks made while the button stayed held, or a second click
/// within the same 1/20 s tick as the miss. Most clicking never notices it.
///
/// Minecraft handles clicks at its next tick; we handle them the moment they happen (more
/// responsive) and count as if that tick had already started: each tick first counts down, then
/// handles the clicks made since the last tick, then clears the penalty if the button is up.
/// </summary>
public sealed class MissPenalty
{
    /// <summary>leftClickCounter as it stands before the coming tick counts it down.</summary>
    private int _counter;

    /// <summary>Whether a click now would be thrown away (no swing, no attack).</summary>
    public bool Blocks => _counter > 1;

    /// <summary>A click hit nothing.</summary>
    public void Missed() => _counter = Tuning.MissClickCooldownMcTicks + 1;

    /// <summary>A Minecraft tick: count down, then clear it if the attack button is up.</summary>
    public void Tick(bool buttonHeld)
    {
        if (_counter > 0)
        {
            _counter--;
        }
        if (!buttonHeld)
        {
            _counter = 0;
        }
    }

    public void Reset() => _counter = 0;
}
