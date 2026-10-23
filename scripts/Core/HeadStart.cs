using System;

namespace UhcPvp.Core;

/// <summary>
/// The head start (1.8.9's display delay, see <see cref="Tuning.McHeadStart"/>; a new match starts
/// with <see cref="Tuning.DefaultHeadStart"/>): every game draws other players, and hit-tests them,
/// a fixed time in the past. Whoever is chasing a player they just hit sees them a little closer
/// than they are, and is seen a little farther away, so they get back in reach first. See
/// docs/netcode.md.
/// </summary>
public static class HeadStart
{
    /// <summary>
    /// How far in the past a player is shown (seconds), with the match's
    /// <paramref name="headStart"/>. For a player this game runs, never less than one tick, so
    /// they move smoothly between ticks. For a player whose updates come over the network,
    /// <paramref name="networkDelay"/> is what their updates need to arrive in time
    /// (<see cref="SnapshotBuffer.RenderDelay"/>): it counts toward the head start, so the total
    /// stays the same at any ping, until the network alone needs more.
    /// </summary>
    public static double ViewDelay(double headStart, bool owned, double networkDelay) =>
        Math.Max(headStart, owned ? 1.0 / Tuning.SimRate : networkDelay);

    /// <summary>
    /// The oldest view of a victim a hit may come from (seconds): the head start, or the lag cap
    /// if that's more. Older means the attacker's connection fell behind the head start by more
    /// than the lag cap allows: a ghost hit.
    /// </summary>
    public static double MaxViewAge(double headStart) => Math.Max(headStart, Tuning.LagCap);

    /// <summary>"175 ms", or "Off" for 0.</summary>
    public static string Label(double headStart) => headStart <= 0 ? "Off" : $"{Math.Round(headStart * 1000):0} ms";
}
