using System;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;

namespace UhcPvp.Tests;

/// <summary>The player's settings: defaults, limits, and reading and writing the settings file.</summary>
public class SettingsTests : TestCase
{
    public void TestDefaultsKeepTheGameAsItWas()
    {
        GameSettings d = GameSettings.Default;
        CheckEqual(d.MaxFps, GameSettings.Unlimited, "no frame cap by default");
        CheckEqual(d.VolumePercent, 100, "full volume by default");
        CheckNear(d.VolumeDb, 0, 1e-12, "full volume leaves sounds as they are");
        CheckEqual(d.Normalized(), d, "the defaults are valid");
    }

    public void TestEveryOfferedChoiceIsKept()
    {
        foreach (int fps in GameSettings.MaxFpsChoices)
        {
            CheckEqual(new GameSettings(fps, 50).Normalized().MaxFps, fps, $"cap {GameSettings.MaxFpsLabel(fps)}");
        }
        CheckEqual(GameSettings.MaxFpsChoices[^1], GameSettings.Unlimited, "Unlimited comes last in the list");
        CheckEqual(GameSettings.MaxFpsLabel(GameSettings.Unlimited), "Unlimited", "label for no cap");
        CheckEqual(GameSettings.MaxFpsLabel(144), "144", "label for a cap");
    }

    public void TestOddValuesBecomeValid()
    {
        CheckEqual(new GameSettings(100, 50).Normalized().MaxFps, 120, "a cap that isn't offered snaps to the nearest");
        CheckEqual(new GameSettings(5, 50).Normalized().MaxFps, 30, "too low becomes the lowest cap");
        CheckEqual(new GameSettings(5000, 50).Normalized().MaxFps, 360, "too high becomes the highest cap");
        CheckEqual(new GameSettings(-3, 50).Normalized().MaxFps, GameSettings.Unlimited, "negative means no cap");
        CheckEqual(new GameSettings(60, 150).Normalized().VolumePercent, 100, "volume above 100");
        CheckEqual(new GameSettings(60, -20).Normalized().VolumePercent, 0, "volume below 0");
    }

    public void TestVolumeIsLinearLikeMinecraft()
    {
        CheckNear(new GameSettings(0, 50).VolumeDb, 20 * Math.Log10(0.5), 1e-12, "50% is half the loudness scale (-6 dB)");
        Check(double.IsNegativeInfinity(new GameSettings(0, 0).VolumeDb), "0% is silent");
    }

    public void TestSettingsFileRoundTrip()
    {
        var config = new ConfigFile();
        config.SetValue("player", "name", "Alice");
        var settings = new GameSettings(144, 35);
        Settings.Write(config, settings);
        var reloaded = new ConfigFile();
        CheckEqual(reloaded.Parse(config.EncodeToText()), Error.Ok, "the file parses");
        CheckEqual(Settings.Read(reloaded), settings, "settings come back the same");
        CheckEqual(reloaded.GetValue("player", "name", "").AsString(), "Alice", "the name in the same file is kept");
    }

    public void TestMissingOrBrokenFileFallsBackToDefaults()
    {
        CheckEqual(Settings.Read(new ConfigFile()), GameSettings.Default, "no file");
        var broken = new ConfigFile();
        broken.SetValue("video", "max_fps", "lots");
        broken.SetValue("audio", "volume", double.NaN);
        CheckEqual(Settings.Read(broken), GameSettings.Default, "text and NaN instead of numbers");
        var edited = new ConfigFile();
        edited.SetValue("video", "max_fps", 59.6);
        edited.SetValue("audio", "volume", 400);
        CheckEqual(Settings.Read(edited), new GameSettings(60, 100), "hand-edited numbers are made valid");
    }

    public void TestMatchRulesStayValid()
    {
        CheckEqual(MatchRules.Default.StrafeSpeed, 1.0, "plain 1.8 by default");
        CheckEqual(new MatchRules(1.3).Normalized().StrafeSpeed, 1.3, "a slider step is kept");
        CheckEqual(new MatchRules(1.32).Normalized().StrafeSpeed, 1.3, "rounded to the slider's 5% steps");
        CheckEqual(new MatchRules(3.0).Normalized().StrafeSpeed, Tuning.MaxStrafeSpeed, "capped at the maximum");
        CheckEqual(new MatchRules(0.5).Normalized().StrafeSpeed, 1.0, "never slower than 1.8");
        CheckEqual(new MatchRules(double.NaN).Normalized(), MatchRules.Default, "not a number");
        CheckEqual(new Net.RulesUpdate(1.15f).ToMatchRules().StrafeSpeed, 1.15, "a float on the wire comes back as the exact step");
        CheckEqual(new Net.RulesUpdate(float.PositiveInfinity).ToMatchRules(), MatchRules.Default, "bad value on the wire");
        CheckEqual(MatchRules.Percent(1.15), "115%", "shown as a percentage");
    }

    public void TestHostRulesAreKeptInTheSettingsFile()
    {
        var config = new ConfigFile();
        Settings.Write(config, new GameSettings(144, 35));
        Settings.WriteRules(config, new MatchRules(1.35));
        var reloaded = new ConfigFile();
        CheckEqual(reloaded.Parse(config.EncodeToText()), Error.Ok, "the file parses");
        CheckEqual(Settings.ReadRules(reloaded), new MatchRules(1.35), "the rules come back");
        CheckEqual(Settings.Read(reloaded), new GameSettings(144, 35), "next to the other settings");
        CheckEqual(Settings.ReadRules(new ConfigFile()), MatchRules.Default, "no file: plain 1.8");
        var broken = new ConfigFile();
        broken.SetValue("rules", "strafe_speed", "fast");
        CheckEqual(Settings.ReadRules(broken), MatchRules.Default, "text instead of a number");
    }
}
