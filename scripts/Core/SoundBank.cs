using System;
using System.Collections.Generic;

namespace UhcPvp.Core;

/// <summary>
/// One sound effect: its files (res://assets/sounds/NAME_1.wav and on, made by
/// tools/gen_sounds.py) and how it plays.
/// </summary>
/// <param name="Name">File name stem.</param>
/// <param name="Variants">How many files: a different one plays each time.</param>
/// <param name="VolumeDb">Loudness in the mix, as heard from a block away (every file is mastered to the same loudness).</param>
/// <param name="PitchSpread">Each play's pitch is up to this fraction higher or lower (most plays land near the middle).</param>
/// <param name="VolumeSpreadDb">Each play is up to this many dB louder or quieter.</param>
/// <param name="UnitSize">Heard from a place in the world: full volume within this many blocks, then 6 dB quieter
/// (and a little muffled) each time the distance doubles.</param>
/// <param name="MaxDistance">Not heard at all beyond this many blocks.</param>
/// <param name="MaxVoices">At most this many play at once; another one cuts the oldest.</param>
public sealed record SoundSpec(
    string Name, int Variants, double VolumeDb, double PitchSpread, double VolumeSpreadDb,
    double UnitSize, double MaxDistance, int MaxVoices)
{
    public string PathOf(int variant) => $"res://assets/sounds/{Name}_{variant + 1}.wav";
}

/// <summary>
/// Every sound in the game and the mix. The fight sounds are one family (docs/combat.md,
/// "Sounds"): a sword hit is the victim's hurt sound, plus the attacker's own hit sound in their
/// head, plus the crit sound on top when it's a critical.
/// </summary>
public static class SoundBank
{
    public static readonly SoundSpec Swing = new("swing", 8, -10, 0.08, 2.0, 3, 32, 8);
    public static readonly SoundSpec Hit = new("hit", 8, -3, 0.03, 1.0, 0, 0, 4);
    public static readonly SoundSpec Hurt = new("hurt", 8, 0, 0.06, 1.5, 6, 64, 8);
    public static readonly SoundSpec Crit = new("crit", 4, -4, 0.05, 1.0, 6, 64, 4);
    public static readonly SoundSpec RodCast = new("rod_cast", 4, -6, 0.06, 1.0, 5, 48, 6);
    public static readonly SoundSpec BobberHit = new("bobber_hit", 4, -2, 0.05, 1.0, 6, 64, 6);
    public static readonly SoundSpec BobberLand = new("bobber_land", 4, -10, 0.1, 2.0, 3, 32, 6);
    public static readonly SoundSpec Step = new("step", 8, -11, 0.1, 3.0, 5, 40, 12);
    public static readonly SoundSpec Land = new("land", 4, -12, 0.08, 2.0, 4, 40, 8);

    public static IReadOnlyList<SoundSpec> All { get; } =
        new[] { Swing, Hit, Hurt, Crit, RodCast, BobberHit, BobberLand, Step, Land };

    /// <summary>Your own swings, a little under other players' (you hear yours many times a second).</summary>
    public const double OwnSwingDb = -2;
    /// <summary>A swing that cuts only air (nobody in reach): quieter and thinner than one that meets a player.</summary>
    public const double WhiffDb = -4;
    public const double WhiffPitch = 1.08;
    /// <summary>Your own footsteps and landings, well under other players', so theirs stand out.</summary>
    public const double OwnStepsDb = -9;
    /// <summary>The killing blow: the hurt sound lower and heavier.</summary>
    public const double KillPitch = 0.78;
    public const double KillDb = 2;

    /// <summary>Each hit of a combo sounds this much higher than the one before (the hit sound in the attacker's head)...</summary>
    public const double ComboPitchStep = 0.025;
    /// <summary>...for this many hits, then it stays there.</summary>
    public const int ComboPitchSteps = 5;

    /// <summary>The pitch of the attacker's hit sound for the <paramref name="hit"/>th hit of a combo (1 is the first).</summary>
    public static double ComboPitch(int hit) => 1 + ComboPitchStep * Math.Clamp(hit - 1, 0, ComboPitchSteps);

    /// <summary>
    /// How much louder a landing is after falling <paramref name="blocks"/>: a sprint jump's 1.25
    /// blocks is the base, 3 dB softer or louder for each halving or doubling of the drop, from
    /// 5 dB softer to 6 dB louder.
    /// </summary>
    public static double LandingDb(double blocks) =>
        Math.Clamp(3 * Math.Log2(Math.Max(blocks, 0.01) / 1.25), -5, 6);
}

/// <summary>
/// Picks which file plays and how, so a sound repeated many times a second never sounds the
/// same twice running: files come in a shuffled order (each once per round, never the same one
/// twice in a row, even across rounds), and pitch and volume move a little each time, mostly
/// near the middle (the spread is triangular, like Minecraft's random pitch).
/// </summary>
public sealed class SoundVariation
{
    private readonly SoundSpec _sound;
    private readonly Random _random;
    private readonly int[] _order;
    private int _next;
    private int _last = -1;

    public SoundVariation(SoundSpec sound, Random random)
    {
        _sound = sound;
        _random = random;
        _order = new int[Math.Max(sound.Variants, 1)];
        for (int i = 0; i < _order.Length; i++)
        {
            _order[i] = i;
        }
        _next = _order.Length;
    }

    /// <summary>The next play: which file, its pitch scale and its volume change in dB.</summary>
    public (int Variant, double Pitch, double VolumeDb) Next()
    {
        if (_next >= _order.Length)
        {
            Shuffle();
        }
        int variant = _order[_next++];
        _last = variant;
        double pitch = 1 + _sound.PitchSpread * Triangular();
        double volume = _sound.VolumeSpreadDb * Triangular();
        return (variant, pitch, volume);
    }

    /// <summary>A random number from -1 to 1, most often near 0.</summary>
    private double Triangular() => _random.NextDouble() - _random.NextDouble();

    private void Shuffle()
    {
        for (int i = _order.Length - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
        if (_order.Length > 1 && _order[0] == _last)
        {
            // The last file of the old round never opens the new one.
            int swap = 1 + _random.Next(_order.Length - 1);
            (_order[0], _order[swap]) = (_order[swap], _order[0]);
        }
        _next = 0;
    }
}
