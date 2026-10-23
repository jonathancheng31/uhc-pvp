using System;
using System.Collections.Generic;
using System.Globalization;

namespace UhcPvp.Core;

/// <summary>
/// The player's own settings: a frame rate cap, the volume, how much the camera shakes when
/// you're hit, mouse sensitivity and field of view. The Settings autoload saves them
/// in user://settings.cfg and applies them to the engine; this holds the rules (choices, limits,
/// defaults) so they can be tested without a scene.
/// </summary>
/// <param name="MaxFps">Frame rate cap, one of <see cref="MaxFpsChoices"/>; <see cref="Unlimited"/> (0) means none.</param>
/// <param name="VolumePercent">Master volume, 0 (silent) to 100.</param>
/// <param name="HurtShakePercent">
/// How far the camera tilts when you're hit, as a percentage of Minecraft's (14 degrees). 0 is off.
/// </param>
/// <param name="SensitivityPercent">
/// Mouse sensitivity as Minecraft shows it: 0 to 200%, 100% by default. Turning speed follows
/// Minecraft's curve (<see cref="Tuning.DegreesPerMouseCount"/>), so the same number aims the same.
/// </param>
/// <param name="Fov">Vertical field of view in degrees, Minecraft's range: 30 to 110, 70 ("Normal") by default.</param>
public readonly record struct GameSettings(
    int MaxFps, int VolumePercent, int HurtShakePercent,
    int SensitivityPercent = GameSettings.DefaultSensitivityPercent, int Fov = Tuning.DefaultFov)
{
    /// <summary>Minecraft's "100%": its slider at the middle.</summary>
    public const int DefaultSensitivityPercent = 100;
    public const int MaxSensitivityPercent = 200;

    public const int Unlimited = 0;

    /// <summary>
    /// A quarter of Minecraft's tilt: the owner found the full one too much (0.4.5). Many 1.8 PvP
    /// players turn it off entirely.
    /// </summary>
    public const int DefaultHurtShakePercent = 25;

    /// <summary>The frame rate caps offered, lowest first, then Unlimited. Common screen refresh rates.</summary>
    public static readonly IReadOnlyList<int> MaxFpsChoices = new[] { 30, 60, 75, 120, 144, 165, 240, 360, Unlimited };

    /// <summary>
    /// No frame cap (the game runs 60 ticks a second at any frame rate), full volume, a gentle hurt
    /// tilt, and Minecraft's default sensitivity and field of view.
    /// </summary>
    public static GameSettings Default => new(Unlimited, 100, DefaultHurtShakePercent);

    /// <summary>
    /// The same settings made valid, for values read from a file someone may have edited: a
    /// frame cap that isn't offered snaps to the nearest one (0 or less is Unlimited), and the
    /// rest are kept within their sliders' ranges.
    /// </summary>
    public GameSettings Normalized() => new(
        NearestFpsChoice(MaxFps), Math.Clamp(VolumePercent, 0, 100), Math.Clamp(HurtShakePercent, 0, 100),
        Math.Clamp(SensitivityPercent, 0, MaxSensitivityPercent), Math.Clamp(Fov, Tuning.MinFov, Tuning.MaxFov));

    /// <summary>Degrees the view turns per mouse count, on Minecraft's curve (its slider runs 0 to 1).</summary>
    public double DegreesPerMouseCount => Tuning.DegreesPerMouseCount(SensitivityPercent / (double)MaxSensitivityPercent);

    /// <summary>How much of Minecraft's hurt camera tilt to show, 0 to 1.</summary>
    public double HurtShake => HurtShakePercent / 100.0;

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
