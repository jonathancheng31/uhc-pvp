using System;
using System.Collections.Generic;
using Godot;
using UhcPvp.Core;

namespace UhcPvp.Gameplay;

/// <summary>
/// Plays the sound effects (all our own, from tools/gen_sounds.py; the list and the mix are in
/// <see cref="SoundBank"/>). Three ways to play one:
/// <list type="bullet">
/// <item><see cref="PlayAt"/>: from a place in the world. Louder on the side it comes from, quieter
/// and more muffled further away, and a little muffled from behind, so you can tell where someone is.</item>
/// <item><see cref="PlayNear"/>: your own body's sounds, from your hand (the sword on the right,
/// the rod on the left), turned only slightly to that side.</item>
/// <item><see cref="PlayFlat"/>: in your head, the same in both ears (your own hits landing).</item>
/// </list>
/// Every play picks the next file and nudges pitch and volume (<see cref="SoundVariation"/>).
/// Everything goes through the Master bus, so it follows the volume setting.
/// </summary>
public static class Sounds
{
    /// <summary>
    /// A sound straight ahead in 3D comes out about 3.5 dB quieter than the same sound played
    /// flat (it's shared between the ears); flat sounds are turned down to match, so a
    /// <see cref="SoundSpec.VolumeDb"/> means the same loudness either way.
    /// </summary>
    private const float FlatDb = -3.5f;
    /// <summary>
    /// How strongly other players' sounds lean to the side they come from (times the project's
    /// 50%): a sound straight to your left is about 12 dB louder in your left ear. At 2 the other
    /// ear would go silent, which sounds unnatural on headphones.
    /// </summary>
    private const float PanningStrength = 1.5f;
    /// <summary>Your own hands' sounds lean only a little: a few dB toward the hand.</summary>
    private const float NearPanningStrength = 0.8f;
    /// <summary>
    /// Sounds from further round than this (degrees from where you're facing; Godot's largest
    /// cone) are muffled by <see cref="BehindMuffleDb"/>, as your head would: it tells behind from
    /// in front. Straight to the side stays clear.
    /// </summary>
    private const float BehindAngle = 90f;
    private const float BehindMuffleDb = -12f;
    /// <summary>The range Godot allows for a 3D sound's loudest level (its MaxDb).</summary>
    private const float LowestCapDb = -24f;
    private const float HighestCapDb = 6f;

    private static readonly Random Random = new();
    private static readonly Dictionary<SoundSpec, AudioStream[]> Streams = new();
    private static readonly Dictionary<SoundSpec, SoundVariation> Variations = new();
    private static readonly Dictionary<SoundSpec, List<Node>> Voices = new();

    /// <summary>From a place in the world (a player, a bobber).</summary>
    public static void PlayAt(Node parent, SoundSpec sound, Vector3 position, double volumeDb = 0, double pitch = 1)
    {
        var player = Make3D(sound, volumeDb, pitch, PanningStrength);
        player.Position = position;
        // Facing the way the listener looks, so the emission cone covers what's in front of them
        // and everything behind falls outside it (see BehindAngle).
        Camera3D? camera = parent.GetViewport()?.GetCamera3D();
        if (camera is not null)
        {
            Vector3 forward = -camera.GlobalBasis.Z;
            forward.Y = 0;
            if (forward.LengthSquared() > 1e-6f)
            {
                player.Basis = Basis.LookingAt(-forward.Normalized());
                player.EmissionAngleEnabled = true;
                player.EmissionAngleDegrees = BehindAngle;
                player.EmissionAngleFilterAttenuationDb = BehindMuffleDb;
            }
        }
        Start(parent, sound, player);
    }

    /// <summary>Your own body's sound, from <paramref name="offset"/> in your camera's space (your hand).</summary>
    public static void PlayNear(Camera3D camera, SoundSpec sound, Vector3 offset, double volumeDb = 0, double pitch = 1)
    {
        var player = Make3D(sound, volumeDb, pitch, NearPanningStrength);
        player.Position = offset;
        Start(camera, sound, player);
    }

    /// <summary>In your head: no place, the same in both ears.</summary>
    public static void PlayFlat(Node parent, SoundSpec sound, double volumeDb = 0, double pitch = 1)
    {
        (AudioStream stream, float scale, float db) = Pick(sound, volumeDb, pitch);
        var player = new AudioStreamPlayer { Stream = stream, PitchScale = scale, VolumeDb = db + FlatDb };
        Start(parent, sound, player);
    }

    /// <summary>
    /// A limiter at the end of the Master bus: when many sounds land at once (a team fight), it
    /// turns the moment down instead of letting it clip. Safe to call again.
    /// </summary>
    public static void SetUpBus()
    {
        int master = AudioServer.GetBusIndex("Master");
        for (int i = 0; i < AudioServer.GetBusEffectCount(master); i++)
        {
            if (AudioServer.GetBusEffect(master, i) is AudioEffectHardLimiter)
            {
                return;
            }
        }
        AudioServer.AddBusEffect(master, new AudioEffectHardLimiter { CeilingDb = -0.3f });
    }

    /// <summary>Lets go of the loaded sounds (when a match ends; they load again on the next play).</summary>
    public static void Release()
    {
        Streams.Clear();
        Voices.Clear();
    }

    private static AudioStreamPlayer3D Make3D(SoundSpec sound, double volumeDb, double pitch, float panning)
    {
        (AudioStream stream, float scale, float db) = Pick(sound, volumeDb, pitch);
        return new AudioStreamPlayer3D
        {
            Stream = stream,
            PitchScale = scale,
            VolumeDb = db,
            UnitSize = (float)sound.UnitSize,
            MaxDistance = (float)sound.MaxDistance,
            // Godot keeps turning a sound up as it comes closer than its unit size, and MaxDb caps
            // the result: capping at the sound's own level makes it exactly that loud up close.
            MaxDb = Math.Clamp(db, LowestCapDb, HighestCapDb),
            PanningStrength = panning,
        };
    }

    private static (AudioStream Stream, float Pitch, float VolumeDb) Pick(SoundSpec sound, double volumeDb, double pitch)
    {
        if (!Streams.TryGetValue(sound, out AudioStream[]? streams))
        {
            streams = new AudioStream[sound.Variants];
            for (int i = 0; i < streams.Length; i++)
            {
                streams[i] = GD.Load<AudioStream>(sound.PathOf(i));
            }
            Streams[sound] = streams;
            Variations[sound] = new SoundVariation(sound, Random);
        }
        (int variant, double pitchScale, double jitterDb) = Variations[sound].Next();
        return (streams[variant], (float)(pitchScale * pitch), (float)(sound.VolumeDb + jitterDb + volumeDb));
    }

    /// <summary>Plays it, freeing it when done; if too many of this sound are playing, the oldest stops.</summary>
    private static void Start(Node parent, SoundSpec sound, Node player)
    {
        if (!Voices.TryGetValue(sound, out List<Node>? voices))
        {
            voices = new List<Node>();
            Voices[sound] = voices;
        }
        voices.RemoveAll(v => !GodotObject.IsInstanceValid(v) || v.IsQueuedForDeletion());
        while (voices.Count >= sound.MaxVoices)
        {
            voices[0].QueueFree();
            voices.RemoveAt(0);
        }
        voices.Add(player);
        parent.AddChild(player);
        switch (player)
        {
            case AudioStreamPlayer3D p3:
                p3.Finished += p3.QueueFree;
                p3.Play();
                break;
            case AudioStreamPlayer p2:
                p2.Finished += p2.QueueFree;
                p2.Play();
                break;
        }
    }
}
