using System;
using System.Collections.Generic;
using UhcPvp.Core;

namespace UhcPvp.Movement;

/// <summary>
/// Another player as this game sees them, for the pushing twist: where they stand, how far they
/// moved over the last Minecraft tick (horizontally, in blocks), the side of them we came from
/// while touching (zero if none, see <see cref="Pushing.Side"/>), and which way to go if exactly
/// on top of each other (<see cref="Pushing.TieBreak"/>).
/// </summary>
public readonly record struct PushContact(Vec3d Feet, Vec3d Move, Vec3d Side, Vec3d TieBreak);

/// <summary>
/// Players pushing each other apart, the "Player pushing" twist. 1.8.9 has none: players walk
/// through each other. Every game moves only its own player (and its bots), so each keeps its own
/// player out of the others as they're drawn on its screen, once per Minecraft tick, and trusts
/// the other games to do the same from their side. Bodies count as upright cylinders as wide and
/// tall as the player, so players slide around each other instead of catching on corners.
///
/// Walking into someone (your keys point at them), you ease in until your bodies overlap by
/// <see cref="Tuning.PushSquish"/>, closing a third of what's left each tick, and stop there. Not
/// walking into them (standing, walking past, backing off), you slide out of any overlap. So:
/// running into a standing player stops you at them and shoves them along slowly; two players
/// walking into each other stop, squished a little; and two players caught inside each other
/// (spawning, lag) slide apart. Each side only reacts to where the other is drawn and only
/// follows half of their backing off, so the two games never chase each other's reactions back
/// and forth, which keeps it steady under lag. Below 100% bodies are softer (see
/// <see cref="Tuning.PushThroughSpeed"/>). A player flying from a hit is left out altogether
/// (<see cref="PlayerMotor.KnockbackFlight"/>), so knockback always stays whole.
/// </summary>
public static class Pushing
{
    /// <summary>Most players one push is worked out against (a full 6v6 match is 11 others).</summary>
    private const int MaxContacts = 16;

    /// <summary>
    /// The push (Minecraft units: blocks per tick, added to the motion at a Minecraft tick's start)
    /// on a player at <paramref name="self"/> from everyone in <paramref name="others"/>.
    /// <paramref name="motion"/> is the player's motion and <paramref name="accel"/> this tick's
    /// acceleration from their keys, so this tick's move would be their sum.
    /// <paramref name="strength"/> is the match rule: 0 is off (1.8.9), 1 is solid. Lower, overlaps
    /// part more gently and, below 60%, you can push through someone, slowed.
    /// </summary>
    public static Vec3d Resolve(Vec3d self, Vec3d motion, Vec3d accel, IReadOnlyList<PushContact> others, double strength)
    {
        if (strength <= 0 || others.Count == 0)
        {
            return Vec3d.Zero;
        }
        // Each player nearby sets a limit on how far this tick's move may take us toward them.
        Span<Limit> limits = stackalloc Limit[Math.Min(others.Count, MaxContacts)];
        int count = 0;
        for (int i = 0; i < others.Count && count < limits.Length; i++)
        {
            if (LimitFor(self, accel, others[i], strength) is Limit limit)
            {
                limits[count++] = limit;
            }
        }
        // The smallest push (only ever away from each player) that keeps every limit. With one
        // player that's exact in one pass. With several, each pass shares the corrections out
        // evenly, so limits that can't all be kept (squeezed between two players) meet in the
        // middle instead of fighting from one tick to the next.
        Span<double> given = stackalloc double[count];
        Span<double> more = stackalloc double[count];
        Vec3d move = motion + accel;
        Vec3d push = Vec3d.Zero;
        for (int pass = 0; pass < Tuning.PushPasses; pass++)
        {
            int changed = 0;
            for (int i = 0; i < count; i++)
            {
                double excess = -Dot(move + push, limits[i].Away) - limits[i].Most;
                more[i] = Math.Max(excess, -given[i]);
                if (more[i] != 0)
                {
                    changed++;
                }
            }
            if (changed == 0)
            {
                break;
            }
            for (int i = 0; i < count; i++)
            {
                double share = more[i] / changed;
                given[i] += share;
                push += limits[i].Away * share;
            }
        }
        return push;
    }

    /// <summary>
    /// The side of <paramref name="other"/> that <paramref name="self"/> is on, to remember while
    /// they touch: the direction from them to us, unless our centres have just passed each other
    /// (lag, or squeezing through at a soft setting), in which case the side we came from
    /// (<paramref name="remembered"/>, zero if none) stays. Exactly on top of each other it stays
    /// too, or the tie-break decides.
    /// </summary>
    public static Vec3d Side(Vec3d self, Vec3d other, Vec3d remembered, Vec3d tieBreak)
    {
        Vec3d offset = (self - other).WithY(0);
        double distance = offset.HorizontalLength;
        bool hasMemory = remembered != Vec3d.Zero;
        if (distance < Tuning.PushMinDistance)
        {
            return hasMemory ? remembered : tieBreak;
        }
        Vec3d current = offset * (1 / distance);
        return hasMemory && Dot(current, remembered) < 0 ? remembered : current;
    }

    /// <summary>Whether two players are close enough that the side each came from is worth remembering.</summary>
    public static bool Touching(Vec3d self, Vec3d other)
    {
        double dx = self.X - other.X;
        double dz = self.Z - other.Z;
        double reach = Tuning.PlayerWidth + Tuning.PushMemoryGap;
        return dx * dx + dz * dz < reach * reach && Math.Abs(self.Y - other.Y) < Tuning.PlayerHeight;
    }

    /// <summary>
    /// Which way a player goes when standing exactly on top of another, so both games agree:
    /// the lower id one way along X, the higher the other.
    /// </summary>
    public static Vec3d TieBreak(int selfId, int otherId) => new(selfId < otherId ? -1 : 1, 0, 0);

    /// <summary>The rule for one other player: this tick's move toward them (along <c>Away</c>, which points from them to us) may be at most <c>Most</c>.</summary>
    private readonly record struct Limit(Vec3d Away, double Most);

    private static Limit? LimitFor(Vec3d self, Vec3d accel, PushContact other, double strength)
    {
        if (Math.Abs(self.Y - other.Feet.Y) >= Tuning.PlayerHeight)
        {
            return null;
        }
        Vec3d offset = (self - other.Feet).WithY(0);
        double distance = offset.HorizontalLength;
        if (distance - Tuning.PlayerWidth > Tuning.PushRange)
        {
            return null;
        }
        // Below 100%, bodies are softer: you can keep pushing through someone at up to this
        // speed, and overlaps part more gently.
        double through = strength >= 1 ? 0 : Tuning.PushThroughSpeed * (1 / strength - 1);
        double backOff = Tuning.PushBackOff * strength;
        double yield = Tuning.PushYield * strength;
        double fastest = Tuning.PushMaxSeparation * strength;
        // Firm enough that nobody gets through: then centres that have passed each other (lag can
        // do that) while we still walk into them mean we go back to the side we came from,
        // however deep that makes it. (Softer, getting through is allowed, so let it finish.)
        bool firm = through < fastest / 2;
        bool hasSide = other.Side != Vec3d.Zero;
        Vec3d away = distance >= Tuning.PushMinDistance ? offset * (1 / distance) : hasSide ? other.Side : other.TieBreak;
        double overlap = Tuning.PlayerWidth - distance;
        if (firm && hasSide && Dot(away, other.Side) < 0 && Dot(accel, other.Side) < 0)
        {
            away = other.Side;
            overlap = Tuning.PlayerWidth - Dot(offset, away);
        }
        double gap = -overlap;
        // How fast they're backing off from us: we may follow half of it.
        double retreat = Math.Max(0, -Dot(other.Move, away)) * Tuning.PushFollow;

        // Walking into them: ease in to the squish depth, closing a share of what's left each
        // tick; deeper (lag, or squeezing through when soft), ease back out.
        double pressLimit = retreat + (overlap < Tuning.PushSquish
            ? Math.Max((Tuning.PushSquish - overlap) * Tuning.PushEaseIn, through)
            : through - Math.Min(backOff * (overlap - Tuning.PushSquish), fastest) / 2);
        // Not walking into them: never into them, and out of any overlap (our half of it).
        double yieldLimit = gap >= 0
            ? gap * Tuning.PushEaseIn + retreat
            : -Math.Min(yield * overlap, fastest) / 2;
        // How squarely the keys point at them blends the two, so walking past someone is smooth.
        double keys = accel.HorizontalLength;
        double pressing = keys > 0 ? Math.Clamp(-Dot(accel, away) / keys / Tuning.PushPressCos, 0, 1) : 0;
        return new Limit(away, pressing * pressLimit + (1 - pressing) * yieldLimit);
    }

    private static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Z * b.Z;
}
