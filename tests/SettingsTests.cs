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

    public void TestMinecraftAndTheDefaults()
    {
        MatchRules plain = MatchRules.Minecraft;
        CheckEqual(plain, new MatchRules(1.0, 0.0, 0.0, 0.0, 1.0, 0.175, 0.0), "plain 1.8.9: every twist at 1.8.9's setting");
        Check(plain.IsMinecraft && !plain.IsDefault, "plain 1.8 knows it's plain 1.8, and isn't the defaults");
        CheckEqual(plain.Normalized(), plain, "plain 1.8 is already valid");
        CheckEqual(MatchRules.Default, plain with { StrafeGrip = 1.0, HeadStart = 0.075, RodCooldown = 0.75 },
            "the defaults, the owner's picks from playing 0.4.8: snappiest strafing, a 75 ms head start, the rod's 0.75 s cooldown, the rest 1.8.9's");
        Check(MatchRules.Default.IsDefault && !MatchRules.Default.IsMinecraft, "the defaults know they're the defaults, and aren't plain 1.8");
        CheckEqual(MatchRules.Default.Normalized(), MatchRules.Default, "the defaults are already valid");
        Check(!(MatchRules.Default with { HeadStart = 0.1 }).IsDefault, "one twist moved is no longer the defaults");
    }

    public void TestMatchRulesStayValid()
    {
        MatchRules plain = MatchRules.Minecraft;
        CheckEqual((plain with { StrafeSpeed = 1.3 }).Normalized().StrafeSpeed, 1.3, "a slider step is kept");
        CheckEqual((plain with { StrafeSpeed = 1.32 }).Normalized().StrafeSpeed, 1.3, "rounded to the slider's 5% steps");
        CheckEqual((plain with { StrafeSpeed = 3.0 }).Normalized().StrafeSpeed, Tuning.MaxStrafeSpeed, "capped at the maximum");
        CheckEqual((plain with { StrafeSpeed = 0.5 }).Normalized().StrafeSpeed, 1.0, "never slower than 1.8");
        var nan = new MatchRules(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        CheckEqual(nan.Normalized(), MatchRules.Default, "not a number: the defaults");
        CheckEqual((plain with { StrafeGrip = double.NaN }).Normalized(), plain with { StrafeGrip = Tuning.DefaultStrafeGrip },
            "one broken twist gets its default, the rest stay");
        CheckEqual(new Net.RulesUpdate(1.15f, 0.35f, 0.15f, 0.7f, 0.85f, 0.225f, 1.15f).ToMatchRules(), new MatchRules(1.15, 0.35, 0.15, 0.7, 0.85, 0.225, 1.15), "floats on the wire come back as exact steps");
        CheckEqual(new Net.RulesUpdate(float.PositiveInfinity, float.NaN, -1f, float.NaN, float.NaN, float.NaN, float.NaN).ToMatchRules(),
            MatchRules.Default, "bad values on the wire: the defaults");
        CheckEqual(RulesUpdateRoundTrip(MatchRules.Minecraft), MatchRules.Minecraft, "plain 1.8 crosses the wire");
        CheckEqual(RulesUpdateRoundTrip(MatchRules.Default), MatchRules.Default, "so do the defaults");
        CheckEqual(RulesUpdateRoundTrip(MatchRules.Default with { RodCooldown = 0.35 }).RodCooldown, 0.35, "and a rod cooldown");
        CheckEqual((plain with { StrafeGrip = 2.0, WastedClickDelay = 9.0 }).Normalized(),
            plain with { StrafeGrip = Tuning.MaxStrafeGrip, WastedClickDelay = Tuning.MaxWastedClickDelay }, "grip and click delay are capped");
        CheckEqual((plain with { StrafeGrip = 0.33, WastedClickDelay = 0.12 }).Normalized(),
            plain with { StrafeGrip = 0.35, WastedClickDelay = 0.1 }, "rounded to their 5% and 0.05 s steps");
        Check(!(plain with { WastedClickDelay = 0.1 }).IsMinecraft, "knows when a twist is on");
        CheckEqual(MatchRules.Minecraft.Pushing, 0.0, "players walk through each other in 1.8.9");
        CheckEqual(MatchRules.Default.Pushing, 0.0, "and by default");
        CheckEqual((plain with { Pushing = 0.43 }).Normalized().Pushing, 0.4, "pushing rounds to its 10% steps");
        CheckEqual((plain with { Pushing = 2.0 }).Normalized().Pushing, 1.0, "pushing tops out at solid (100%)");
        CheckEqual((plain with { Pushing = -1.0 }).Normalized().Pushing, 0.0, "pushing never goes below off");
        Check(!(plain with { Pushing = 1.0 }).IsMinecraft, "pushing counts as a twist");
        CheckEqual(MatchRules.Minecraft.ComboKnockback, 1.0, "1.8.9's combo knockback is all of it");
        CheckEqual(MatchRules.Default.ComboKnockback, 1.0, "and so is the default");
        CheckEqual((plain with { ComboKnockback = 0.83 }).Normalized().ComboKnockback, 0.85, "combo knockback in 5% steps");
        CheckEqual((plain with { ComboKnockback = 0.2 }).Normalized().ComboKnockback, Tuning.MinComboKnockback, "never below its minimum");
        CheckEqual((plain with { ComboKnockback = 1.4 }).Normalized().ComboKnockback, 1.0, "never more than 1.8's");
        Check(!(plain with { ComboKnockback = 0.8 }).IsMinecraft, "combo knockback counts as a twist");
        CheckEqual(MatchRules.Minecraft.HeadStart, 0.175, "1.8.9's head start");
        CheckEqual(MatchRules.Default.HeadStart, 0.075, "a shorter one by default");
        CheckEqual((plain with { HeadStart = 0.19 }).Normalized().HeadStart, 0.2, "head start in 25 ms steps");
        CheckEqual((plain with { HeadStart = -1 }).Normalized().HeadStart, 0.0, "no head start at least");
        CheckEqual((plain with { HeadStart = 9 }).Normalized().HeadStart, Tuning.MaxHeadStart, "capped at the maximum");
        Check(!(plain with { HeadStart = 0 }).IsMinecraft, "no head start isn't 1.8.9");
        CheckEqual(MatchRules.Minecraft.RodCooldown, 0.0, "1.8.9's rod has no cooldown");
        CheckEqual(MatchRules.Default.RodCooldown, 0.75, "0.75 s by default, as before it was a twist");
        CheckEqual((plain with { RodCooldown = 0.62 }).Normalized().RodCooldown, 0.6, "rod cooldown in 0.05 s steps (a Minecraft tick)");
        CheckEqual((plain with { RodCooldown = 0.73 }).Normalized().RodCooldown, 0.75, "rounded to the nearest");
        CheckEqual((plain with { RodCooldown = -1 }).Normalized().RodCooldown, 0.0, "off at least");
        CheckEqual((plain with { RodCooldown = 9 }).Normalized().RodCooldown, Tuning.MaxRodCooldown, "capped at the maximum");
        CheckEqual((plain with { RodCooldown = double.PositiveInfinity }).Normalized().RodCooldown, Tuning.DefaultRodCooldown, "not a number: the default");
        Check(!(plain with { RodCooldown = 0.75 }).IsMinecraft, "a rod cooldown counts as a twist");
        for (double seconds = 0; seconds <= Tuning.MaxRodCooldown + 1e-9; seconds += Tuning.RodCooldownStep)
        {
            double snapped = (plain with { RodCooldown = seconds }).Normalized().RodCooldown;
            CheckNear(snapped * Tuning.SimRate, Math.Round(snapped * Tuning.SimRate), 1e-9, $"every step is a whole number of our ticks ({snapped})");
        }
        CheckEqual(HeadStart.Label(0), "Off", "no head start shows as off");
        CheckEqual(HeadStart.Label(0.175), "175 ms", "a head start shows in milliseconds");
        CheckEqual(MatchRules.Seconds(0), "Off", "a delay of 0 shows as off");
        CheckEqual(MatchRules.Seconds(0.15), "0.15 s", "a delay shows in seconds");
        CheckEqual(MatchRules.Percent(1.15), "115%", "shown as a percentage");
        CheckEqual(MatchRules.PercentOrOff(0), "Off", "a twist at 0% shows as off");
        CheckEqual(MatchRules.PercentOrOff(0.5), "50%", "and otherwise as a percentage");
    }

    public void TestRulesSummary()
    {
        static string Plain(string text) => text.Replace('\u00A0', ' ');
        CheckEqual(MatchRules.Default.Summary(), "Defaults", "the defaults aren't listed as changes");
        CheckEqual(MatchRules.Minecraft.Summary(), "Plain 1.8 (all off)", "plain 1.8");
        CheckEqual(Plain((MatchRules.Default with { HeadStart = 0.175 }).Summary()), "Defaults, but head start 175 ms",
            "close to the defaults: what's different from them");
        CheckEqual(Plain((MatchRules.Minecraft with { Pushing = 1.0 }).Summary()), "Plain 1.8, but pushing 100%",
            "close to plain 1.8: what's different from it");
        CheckEqual(Plain((MatchRules.Default with { StrafeGrip = 0, Pushing = 0.5, WastedClickDelay = 0.15 }).Summary()),
            "Defaults, but snappiness off, pushing 50%, wasted clicks 0.15 s", "values read as on the Twists screen, in its order");
        CheckEqual(Plain((MatchRules.Default with { RodCooldown = 0 }).Summary()), "Defaults, but rod cooldown off",
            "no rod cooldown shows as off");
        CheckEqual(Plain((MatchRules.Minecraft with { RodCooldown = 1.2 }).Summary()), "Plain 1.8, but rod cooldown 1.20 s",
            "a rod cooldown shows in seconds");
        CheckEqual(Plain(new MatchRules(1.3, 0.5, 0.15, 1.0, 0.8, 0.2, 1.2).Summary()),
            "Defaults, but strafe speed 130%, snappiness 50%, pushing 100%, combo knockback 80%, head start 200 ms, wasted clicks 0.15 s, rod cooldown 1.20 s",
            "everything moved: from the defaults");
        Check(!(MatchRules.Default with { HeadStart = 0.175 }).Summary().Contains("head start 175", StringComparison.Ordinal),
            "each change stays on one line when the summary wraps");
    }

    private static MatchRules RulesUpdateRoundTrip(MatchRules rules) => Net.RulesUpdate.From(rules).ToMatchRules();

    public void TestHostRulesAreKeptInTheSettingsFile()
    {
        var config = new ConfigFile();
        Settings.Write(config, new GameSettings(144, 35, 25));
        Settings.WriteRules(config, new MatchRules(1.35, 0.6, 0.2, 0.8, 0.7, 0.1, 1.1));
        var reloaded = new ConfigFile();
        CheckEqual(reloaded.Parse(config.EncodeToText()), Error.Ok, "the file parses");
        CheckEqual(Settings.ReadRules(reloaded), new MatchRules(1.35, 0.6, 0.2, 0.8, 0.7, 0.1, 1.1), "the rules come back");
        CheckEqual(Settings.Read(reloaded), new GameSettings(144, 35, 25), "next to the other settings");
        CheckEqual(Settings.ReadRules(new ConfigFile()), MatchRules.Default, "no file: the defaults");
        var broken = new ConfigFile();
        broken.SetValue("rules", "strafe_speed", "fast");
        CheckEqual(Settings.ReadRules(broken), MatchRules.Default, "text instead of a number");

        // A host who saved plain 1.8 keeps it (for a file from before the rod cooldown was a twist, see below).
        var plain = new ConfigFile();
        Settings.WriteRules(plain, MatchRules.Minecraft);
        CheckEqual(Settings.ReadRules(plain), MatchRules.Minecraft, "a saved plain 1.8 stays plain 1.8");
        // A file from 0.4.7 has no combo knockback or head start yet: those get the defaults.
        var old = new ConfigFile();
        old.SetValue("rules", "strafe_speed", 1.2);
        old.SetValue("rules", "strafe_grip", 0.0);
        old.SetValue("rules", "wasted_click_delay", 0.0);
        old.SetValue("rules", "pushing", 1.0);
        CheckEqual(Settings.ReadRules(old),
            new MatchRules(1.2, 0.0, 0.0, 1.0, Tuning.DefaultComboKnockback, Tuning.DefaultHeadStart, Tuning.DefaultRodCooldown),
            "saved twists stay, missing ones get the defaults");
        // A file from 0.4.9 has no rod cooldown yet: the rod keeps the 0.75 s it always had.
        var beforeRodTwist = new ConfigFile();
        Settings.WriteRules(beforeRodTwist, MatchRules.Minecraft);
        beforeRodTwist.EraseSectionKey("rules", "rod_cooldown");
        CheckEqual(Settings.ReadRules(beforeRodTwist), MatchRules.Minecraft with { RodCooldown = 0.75 },
            "a saved plain 1.8 from before keeps the rod's cooldown");
    }

    public void TestBotSkillIsKeptInTheSettingsFile()
    {
        CheckEqual(Tuning.DefaultBotDifficulty, BotDifficulty.Normal, "Normal by default");
        CheckEqual(Settings.ReadBotDifficulty(new ConfigFile()), BotDifficulty.Normal, "no file: Normal");
        foreach (BotDifficulty skill in Enum.GetValues<BotDifficulty>())
        {
            var config = new ConfigFile();
            Settings.Write(config, new GameSettings(144, 35, 25));
            Settings.WriteRules(config, new MatchRules(1.35, 0.6, 0.2, 0.8, 0.7, 0.1, 1.1));
            Settings.WriteBotDifficulty(config, skill);
            var reloaded = new ConfigFile();
            CheckEqual(reloaded.Parse(config.EncodeToText()), Error.Ok, $"the file parses ({skill})");
            CheckEqual(Settings.ReadBotDifficulty(reloaded), skill, $"{skill} comes back");
            CheckEqual(Settings.ReadRules(reloaded), new MatchRules(1.35, 0.6, 0.2, 0.8, 0.7, 0.1, 1.1), $"next to the twists ({skill})");
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
