using Godot;
using UhcPvp.Core;

namespace UhcPvp.Gameplay;

/// <summary>
/// Which sound plays for what, and who hears it from where (docs/combat.md, "Sounds"). Your own
/// hits landing sound in your head; everything else comes from where it happens. Footsteps and
/// landings are in <see cref="GameAudio"/>.
/// </summary>
public partial class Game
{
    /// <summary>Your sword hand and rod hand, in your camera's space: where your own swing and cast come from.</summary>
    private static readonly Vector3 SwordHand = new(0.45f, -0.4f, -0.6f);
    private static readonly Vector3 RodHand = new(-0.5f, -0.4f, -0.6f);
    /// <summary>Another player's sword hand, from their feet when facing -Z: right, chest high, a little forward.</summary>
    private static readonly Vector3 TheirSwordHand = new(0.35f, 1.3f, -0.3f);

    /// <summary>
    /// The whoosh of an arm swing, as the swing starts. <paramref name="connected"/>: whether your
    /// swing met a player (a swing at nothing is quieter and thinner), or null for someone else's.
    /// </summary>
    private void PlaySwingSound(Player swinger, bool? connected)
    {
        if (swinger.IsLocal && swinger.Camera is Camera3D camera)
        {
            bool whiff = connected == false;
            Sounds.PlayNear(camera, SoundBank.Swing, SwordHand,
                SoundBank.OwnSwingDb + (whiff ? SoundBank.WhiffDb : 0), whiff ? SoundBank.WhiffPitch : 1);
        }
        else
        {
            Vector3 hand = TheirSwordHand.Rotated(Vector3.Up, Mathf.DegToRad(swinger.Yaw));
            Sounds.PlayAt(this, SoundBank.Swing, swinger.Position + hand);
        }
    }

    /// <summary>The rod's throw, as the bobber leaves the rod: yours from your rod hand, others' from theirs.</summary>
    private void PlayCastSound(Player thrower, Vector3 origin)
    {
        if (thrower.IsLocal && thrower.Camera is Camera3D camera)
        {
            Sounds.PlayNear(camera, SoundBank.RodCast, RodHand);
        }
        else
        {
            Sounds.PlayAt(this, SoundBank.RodCast, origin);
        }
    }

    /// <summary>
    /// A blow landing on <paramref name="victim"/>. Everyone hears the hurt sound from them (lower
    /// and heavier on a kill) and the crit on top of a critical. Whoever struck it hears their own
    /// hit in their head, a little higher with each hit of their combo (<paramref name="comboHit"/>
    /// is this hit's number in it); a bobber catching someone is its own sound, in the thrower's
    /// head and from the victim for everyone else.
    /// </summary>
    private void PlayBlowSounds(Player victim, bool byMe, bool fresh, bool killed, bool critical, bool rod, int comboHit)
    {
        Vector3 chest = Chest(victim);
        if (fresh || killed)
        {
            PlayHurtSound(victim, killed);
        }
        if (rod && byMe)
        {
            Sounds.PlayFlat(this, SoundBank.BobberHit);
        }
        else if (rod)
        {
            Sounds.PlayAt(this, SoundBank.BobberHit, chest);
        }
        else if (byMe)
        {
            Sounds.PlayFlat(this, SoundBank.Hit, 0, SoundBank.ComboPitch(comboHit));
        }
        if (critical)
        {
            Sounds.PlayAt(this, SoundBank.Crit, chest);
        }
    }

    private void PlayHurtSound(Player victim, bool killed) =>
        Sounds.PlayAt(this, SoundBank.Hurt, Chest(victim),
            killed ? SoundBank.KillDb : 0, killed ? SoundBank.KillPitch : 1);

    /// <summary>A bobber touching down on the ground or a wall, heard from where it lands, on the frame it's drawn there.</summary>
    private void PlayBobberLandSound(BobberView view, double shownTime, Vector3 position)
    {
        if (view.Bobber.LandedAt is double landedAt && shownTime >= landedAt && view.HeardLandingAt != landedAt)
        {
            view.HeardLandingAt = landedAt;
            Sounds.PlayAt(this, SoundBank.BobberLand, position);
        }
    }
}
