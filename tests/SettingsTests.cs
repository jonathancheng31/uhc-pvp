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

    public void TestSensitivityAndFovFollowMinecraft()
    {
        GameSettings d = GameSettings.Default;
        CheckEqual(d.SensitivityPercent, 100, "Minecraft's 100% by default");
        CheckNear(d.DegreesPerMouseCount, Tuning.DegreesPerMouseCount(0.5), 1e-12, "100% is Minecraft's slider at the middle");
        CheckNear((d with { SensitivityPercent = 200 }).DegreesPerMouseCount, Tuning.DegreesPerMouseCount(1.0), 1e-12, "200% is the slider all the way right");
        CheckNear((d with { SensitivityPercent = 0 }).DegreesPerMouseCount, Tuning.DegreesPerMouseCount(0.0), 1e-12, "0% still turns, slowly, like Minecraft");
        Check((d with { SensitivityPercent = 0 }).DegreesPerMouseCount > 0, "0% isn't frozen");
        CheckEqual(d.Fov, 70, "Minecraft's normal field of view by default");
        CheckEqual((d with { SensitivityPercent = 500, Fov = 170 }).Normalized(), d with { SensitivityPercent = 200, Fov = 110 }, "capped at Minecraft's maximums");
        CheckEqual((d with { SensitivityPercent = -1, Fov = 5 }).Normalized(), d with { SensitivityPercent = 0, Fov = 30 }, "and its minimums");
        var config = new ConfigFile();
        Settings.Write(config, d with { SensitivityPercent = 87, Fov = 90 });
        var reloaded = new ConfigFile();
        reloaded.Parse(config.EncodeToText());
        CheckEqual(Settings.Read(reloaded), d with { SensitivityPercent = 87, Fov = 90 }, "kept in the file");
        var old = new ConfigFile();
        old.SetValue("video", "max_fps", 144);
        CheckEqual(Settings.Read(old) with { MaxFps = 0 }, d, "a file from before 0.4.7 gets Minecraft's defaults");
    }

    public void TestHandLooksTheSameAtAnyFov()
    {
        CheckNear(Gameplay.ViewModel.HandScale(Tuning.HandFov), 1, 1e-6, "no stretch at the hand's own view");
        foreach (float fov in new[] { 30f, 90f, 110f })
        {
            // A point of the sword, seen through the player's camera after the stretch, lands where
            // the hand's own 70-degree view would put it.
            float k = Gameplay.ViewModel.HandScale(fov);
            float x = 0.3f, y = -0.25f, depth = 0.6f;
            float shown = k * y / (depth * MathF.Tan(Godot.Mathf.DegToRad(fov) / 2));
            float hand = y / (depth * MathF.Tan(Godot.Mathf.DegToRad(Tuning.HandFov) / 2));
            CheckNear(shown, hand, 1e-6, $"up and down at {fov}");
            CheckNear(k * x / (depth * MathF.Tan(Godot.Mathf.DegToRad(fov) / 2)), x / (depth * MathF.Tan(Godot.Mathf.DegToRad(Tuning.HandFov) / 2)), 1e-6, $"sideways at {fov}");
        }
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
        CheckEqual(new Net.RulesUpdate(1.15f, 0.35f, 0.15f, 0.7f, 0.85f, 0.225f).ToMatchRules(), new MatchRules(1.15, 0.35, 0.15, 0.7, 0.85, 0.225), "floats on the wire come back as exact steps");
        CheckEqual(new Net.RulesUpdate(float.PositiveInfinity, float.NaN, -1f, float.NaN, float.NaN, float.NaN).ToMatchRules(), MatchRules.Default, "bad values on the wire");
        CheckEqual(new MatchRules(1.0, 2.0, 9.0).Normalized(), new MatchRules(1.0, Tuning.MaxStrafeGrip, Tuning.MaxWastedClickDelay), "grip and click delay are capped");
        CheckEqual(new MatchRules(1.0, 0.33, 0.12).Normalized(), new MatchRules(1.0, 0.35, 0.1), "rounded to their 5% and 0.05 s steps");
        Check(MatchRules.Default.IsMinecraft && !new MatchRules(1.0, 0.0, 0.1).IsMinecraft, "knows when every twist is off");
        CheckEqual(MatchRules.Default.Pushing, 0.0, "players walk through each other by default, like 1.8.9");
        CheckEqual(new MatchRules(Pushing: 0.43).Normalized().Pushing, 0.4, "pushing rounds to its 10% steps");
        CheckEqual(new MatchRules(Pushing: 2.0).Normalized().Pushing, 1.0, "pushing tops out at solid (100%)");
        CheckEqual(new MatchRules(Pushing: -1.0).Normalized().Pushing, 0.0, "pushing never goes below off");
        Check(!new MatchRules(Pushing: 1.0).IsMinecraft, "pushing counts as a twist");
        CheckEqual(MatchRules.Default.ComboKnockback, 1.0, "combo knockback is 1.8's by default");
        CheckEqual(new MatchRules(ComboKnockback: 0.83).Normalized().ComboKnockback, 0.85, "combo knockback in 5% steps");
        CheckEqual(new MatchRules(ComboKnockback: 0.2).Normalized().ComboKnockback, Tuning.MinComboKnockback, "never below its minimum");
        CheckEqual(new MatchRules(ComboKnockback: 1.4).Normalized().ComboKnockback, 1.0, "never more than 1.8's");
        Check(!new MatchRules(ComboKnockback: 0.8).IsMinecraft, "combo knockback counts as a twist");
        CheckEqual(MatchRules.Default.HeadStart, 0.175, "1.8.9's head start by default");
        CheckEqual(new MatchRules(HeadStart: 0.19).Normalized().HeadStart, 0.2, "head start in 25 ms steps");
        CheckEqual(new MatchRules(HeadStart: -1).Normalized().HeadStart, 0.0, "no head start at least");
        CheckEqual(new MatchRules(HeadStart: 9).Normalized().HeadStart, Tuning.MaxHeadStart, "capped at the maximum");
        Check(!new MatchRules(HeadStart: 0).IsMinecraft, "no head start isn't 1.8.9");
        CheckEqual(HeadStart.Label(0), "Off", "no head start shows as off");
        CheckEqual(HeadStart.Label(0.175), "175 ms", "a head start shows in milliseconds");
        CheckEqual(MatchRules.Seconds(0), "Off", "a delay of 0 shows as off");
        CheckEqual(MatchRules.Seconds(0.15), "0.15 s", "a delay shows in seconds");
        CheckEqual(MatchRules.Percent(1.15), "115%", "shown as a percentage");
    }

    public void TestHostRulesAreKeptInTheSettingsFile()
    {
        var config = new ConfigFile();
        Settings.Write(config, new GameSettings(144, 35, 25));
        Settings.WriteRules(config, new MatchRules(1.35, 0.6, 0.2, 0.8, 0.7, 0.1));
        var reloaded = new ConfigFile();
        CheckEqual(reloaded.Parse(config.EncodeToText()), Error.Ok, "the file parses");
        CheckEqual(Settings.ReadRules(reloaded), new MatchRules(1.35, 0.6, 0.2, 0.8, 0.7, 0.1), "the rules come back");
        CheckEqual(Settings.Read(reloaded), new GameSettings(144, 35, 25), "next to the other settings");
        CheckEqual(Settings.ReadRules(new ConfigFile()), MatchRules.Default, "no file: plain 1.8");
        var broken = new ConfigFile();
        broken.SetValue("rules", "strafe_speed", "fast");
        CheckEqual(Settings.ReadRules(broken), MatchRules.Default, "text instead of a number");
    }

    public void TestBotSkillIsKeptInTheSettingsFile()
    {
        CheckEqual(Tuning.DefaultBotDifficulty, BotDifficulty.Normal, "Normal by default");
        CheckEqual(Settings.ReadBotDifficulty(new ConfigFile()), BotDifficulty.Normal, "no file: Normal");
        foreach (BotDifficulty skill in Enum.GetValues<BotDifficulty>())
        {
            var config = new ConfigFile();
            Settings.Write(config, new GameSettings(144, 35, 25));
            Settings.WriteRules(config, new MatchRules(1.35, 0.6, 0.2, 0.8));
            Settings.WriteBotDifficulty(config, skill);
            var reloaded = new ConfigFile();
            CheckEqual(reloaded.Parse(config.EncodeToText()), Error.Ok, $"the file parses ({skill})");
            CheckEqual(Settings.ReadBotDifficulty(reloaded), skill, $"{skill} comes back");
            CheckEqual(Settings.ReadRules(reloaded), new MatchRules(1.35, 0.6, 0.2, 0.8), $"next to the twists ({skill})");
            CheckEqual(Settings.Read(reloaded), new GameSettings(144, 35, 25), $"and the other settings ({skill})");
        }
        var hand = new ConfigFile();
        hand.SetValue("practice", "bot_skill", "HARD");
        CheckEqual(Settings.ReadBotDifficulty(hand), BotDifficulty.Hard, "any capitalization");
        foreach (Variant bad in new[] { Variant.From("expert"), Variant.From("7"), Variant.From(2), Variant.From("") })
        {
            var broken = new ConfigFile();
            broken.SetValue("practice", "bot_skill", bad);
            CheckEqual(Settings.ReadBotDifficulty(broken), BotDifficulty.Normal, $"'{bad}' falls back to Normal");
        }
    }
}
