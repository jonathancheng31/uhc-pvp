using System;
using System.Collections.Generic;
using System.Globalization;

namespace UhcPvp.Core;

/// <summary>
/// The player's own settings: a frame rate cap and the volume. The Settings autoload saves them
/// in user://settings.cfg and applies them to the engine; this holds the rules (choices, limits,
/// defaults) so they can be tested without a scene.
/// </summary>
/// <param name="MaxFps">Frame rate cap, one of <see cref="MaxFpsChoices"/>; <see cref="Unlimited"/> (0) means none.</param>
/// <param name="VolumePercent">Master volume, 0 (silent) to 100.</param>
public readonly record struct GameSettings(int MaxFps, int VolumePercent)
{
    public const int Unlimited = 0;

    /// <summary>The frame rate caps offered, lowest first, then Unlimited. Common screen refresh rates.</summary>
    public static readonly IReadOnlyList<int> MaxFpsChoices = new[] { 30, 60, 75, 120, 144, 165, 240, 360, Unlimited };

    /// <summary>No frame cap (the game runs 60 ticks a second at any frame rate) and full volume.</summary>
    public static GameSettings Default => new(Unlimited, 100);

    /// <summary>
    /// The same settings made valid, for values read from a file someone may have edited: a
    /// frame cap that isn't offered snaps to the nearest one (0 or less is Unlimited), and the
    /// volume is kept within 0 to 100.
    /// </summary>
    public GameSettings Normalized() => new(NearestFpsChoice(MaxFps), Math.Clamp(VolumePercent, 0, 100));

    /// <summary>The volume as a level in decibels: the slider scales loudness linearly, like Minecraft's. Silent at 0.</summary>
    public double VolumeDb => VolumePercent <= 0 ? double.NegativeInfinity : 20 * Math.Log10(VolumePercent / 100.0);

    public static string MaxFpsLabel(int maxFps) =>
        maxFps == Unlimited ? "Unlimited" : maxFps.ToString(CultureInfo.InvariantCulture);

    private static int NearestFpsChoice(int maxFps)
    {
        if (maxFps <= 0)
        {
            return Unlimited;
        }
        int best = MaxFpsChoices[0];
        foreach (int choice in MaxFpsChoices)
        {
            if (choice != Unlimited && Math.Abs(choice - maxFps) < Math.Abs(best - maxFps))
            {
                best = choice;
            }
        }
        return best;
    }
}
