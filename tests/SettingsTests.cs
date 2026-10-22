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
        CheckEqual(d.HurtShakePercent, GameSettings.DefaultHurtShakePercent, "the gentle hurt tilt by default");
        CheckNear(d.VolumeDb, 0, 1e-12, "full volume leaves sounds as they are");
        CheckEqual(d.Normalized(), d, "the defaults are valid");
    }

    public void TestHurtShakeIsToneDownAndAdjustable()
    {
        CheckEqual(GameSettings.Default.HurtShakePercent, 25, "a quarter of Minecraft's tilt by default");
        CheckNear(new GameSettings(0, 100, 0).HurtShake, 0, 1e-12, "0% turns it off");
        CheckNear(new GameSettings(0, 100, 100).HurtShake, 1, 1e-12, "100% is Minecraft's");
        CheckEqual(new GameSettings(0, 100, 250).Normalized().HurtShakePercent, 100, "never more than Minecraft's");
        CheckEqual(new GameSettings(0, 100, -5).Normalized().HurtShakePercent, 0, "never below off");
        var config = new ConfigFile();
        Settings.Write(config, new GameSettings(60, 80, 0));
        var reloaded = new ConfigFile();
        reloaded.Parse(config.EncodeToText());
        CheckEqual(Settings.Read(reloaded).HurtShakePercent, 0, "off is kept in the file");
        var old = new ConfigFile();
        old.SetValue("video", "max_fps", 144);
        CheckEqual(Settings.Read(old).HurtShakePercent, 25, "a file from before 0.4.6 gets the gentle default");
    }

    public void TestEveryOfferedChoiceIsKept()
    {
        foreach (int fps in GameSettings.MaxFpsChoices)
        {
            CheckEqual(new GameSettings(fps, 50, 25).Normalized().MaxFps, fps, $"cap {GameSettings.MaxFpsLabel(fps)}");
        }
        CheckEqual(GameSettings.MaxFpsChoices[^1], GameSettings.Unlimited, "Unlimited comes last in the list");
        CheckEqual(GameSettings.MaxFpsLabel(GameSettings.Unlimited), "Unlimited", "label for no cap");
        CheckEqual(GameSettings.MaxFpsLabel(144), "144", "label for a cap");
    }

    public void TestOddValuesBecomeValid()
    {
        CheckEqual(new GameSettings(100, 50, 25).Normalized().MaxFps, 120, "a cap that isn't offered snaps to the nearest");
        CheckEqual(new GameSettings(5, 50, 25).Normalized().MaxFps, 30, "too low becomes the lowest cap");
        CheckEqual(new GameSettings(5000, 50, 25).Normalized().MaxFps, 360, "too high becomes the highest cap");
        CheckEqual(new GameSettings(-3, 50, 25).Normalized().MaxFps, GameSettings.Unlimited, "negative means no cap");
        CheckEqual(new GameSettings(60, 150, 25).Normalized().VolumePercent, 100, "volume above 100");
        CheckEqual(new GameSettings(60, -20, 25).Normalized().VolumePercent, 0, "volume below 0");
    }

    public void TestVolumeIsLinearLikeMinecraft()
    {
        CheckNear(new GameSettings(0, 50, 25).VolumeDb, 20 * Math.Log10(0.5), 1e-12, "50% is half the loudness scale (-6 dB)");
        Check(double.IsNegativeInfinity(new GameSettings(0, 0, 25).VolumeDb), "0% is silent");
    }

    public void TestSettingsFileRoundTrip()
    {
        var config = new ConfigFile();
        config.SetValue("player", "name", "Alice");
        var settings = new GameSettings(144, 35, 25);
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
        CheckEqual(Settings.Read(edited), new GameSettings(60, 100, 25), "hand-edited numbers are made valid");
    }

    public void TestMatchRulesStayValid()
    {
        CheckEqual(MatchRules.Default.StrafeSpeed, 1.0, "plain 1.8 by default");
        CheckEqual(new MatchRules(1.3).Normalized().StrafeSpeed, 1.3, "a slider step is kept");
        CheckEqual(new MatchRules(1.32).Normalized().StrafeSpeed, 1.3, "rounded to the slider's 5% steps");
        CheckEqual(new MatchRules(3.0).Normalized().StrafeSpeed, Tuning.MaxStrafeSpeed, "capped at the maximum");
        CheckEqual(new MatchRules(0.5).Normalized().StrafeSpeed, 1.0, "never slower than 1.8");
        CheckEqual(new MatchRules(double.NaN).Normalized(), MatchRules.Default, "not a number");
        CheckEqual(new Net.RulesUpdate(1.15f, 0.35f, 0.15f).ToMatchRules(), new MatchRules(1.15, 0.35, 0.15), "floats on the wire come back as exact steps");
        CheckEqual(new Net.RulesUpdate(float.PositiveInfinity, float.NaN, -1f).ToMatchRules(), MatchRules.Default, "bad values on the wire");
        CheckEqual(new MatchRules(1.0, 2.0, 9.0).Normalized(), new MatchRules(1.0, Tuning.MaxStrafeGrip, Tuning.MaxWastedClickDelay), "grip and click delay are capped");
        CheckEqual(new MatchRules(1.0, 0.33, 0.12).Normalized(), new MatchRules(1.0, 0.35, 0.1), "rounded to their 5% and 0.05 s steps");
        Check(MatchRules.Default.IsMinecraft && !new MatchRules(1.0, 0.0, 0.1).IsMinecraft, "knows when every twist is off");
        CheckEqual(MatchRules.Seconds(0), "Off", "a delay of 0 shows as off");
        CheckEqual(MatchRules.Seconds(0.15), "0.15 s", "a delay shows in seconds");
        CheckEqual(MatchRules.Percent(1.15), "115%", "shown as a percentage");
    }

    public void TestHostRulesAreKeptInTheSettingsFile()
    {
        var config = new ConfigFile();
        Settings.Write(config, new GameSettings(144, 35, 25));
        Settings.WriteRules(config, new MatchRules(1.35, 0.6, 0.2));
        var reloaded = new ConfigFile();
        CheckEqual(reloaded.Parse(config.EncodeToText()), Error.Ok, "the file parses");
        CheckEqual(Settings.ReadRules(reloaded), new MatchRules(1.35, 0.6, 0.2), "the rules come back");
        CheckEqual(Settings.Read(reloaded), new GameSettings(144, 35, 25), "next to the other settings");
        CheckEqual(Settings.ReadRules(new ConfigFile()), MatchRules.Default, "no file: plain 1.8");
        var broken = new ConfigFile();
        broken.SetValue("rules", "strafe_speed", "fast");
        CheckEqual(Settings.ReadRules(broken), MatchRules.Default, "text instead of a number");
    }
}
