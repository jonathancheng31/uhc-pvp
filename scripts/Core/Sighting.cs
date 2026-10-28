using UhcPvp.Movement;

namespace UhcPvp.Core;

/// <summary>
/// One player as someone on this computer sees them, the head start in the past (see
/// <see cref="HeadStart"/>): where they are, and whether they can be hit or caught there. Both
/// come from the same moment of what's seen, so a player who just respawned can't be hit at
/// their body before the view shows them back at their spawn. A player this game knows has
/// died can't be hit either, even while they're still drawn alive: their own game turns down
/// any hit on them once they're dead.
/// </summary>
public readonly record struct Sighting(Vec3d Feet, bool Hittable)
{
    /// <summary>
    /// Seen in <paramref name="state"/>; <paramref name="knownDead"/>: this game knows they're
    /// dead now.
    /// </summary>
    public static Sighting Of(PlayerState state, bool knownDead) =>
        new(Vec3d.From(state.Position), !knownDead && !state.Has(PlayerFlags.Dead));
}
