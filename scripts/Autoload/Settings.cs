using Godot;
using UhcPvp.Core;

namespace UhcPvp.Autoload;

/// <summary>
/// The player's settings (max FPS, volume, hurt shake, sensitivity, field of view, see <see cref="GameSettings"/>): loaded from
/// user://settings.cfg at startup and applied to the engine. The settings screen
/// (scripts/Ui/SettingsMenu.cs) changes them, which applies at once, and saves when it closes.
/// The same file also keeps the name and address typed in the main menu, and the match rules
/// (twists) and practice-bot skill this player last picked as a host.
/// </summary>
public partial class Settings : Node
{
    public const string FilePath = "user://settings.cfg";

    public static Settings Instance { get; private set; } = null!;

    public GameSettings Current { get; private set; } = GameSettings.Default;

    /// <summary>The match rules this player last picked when hosting; the next match they host starts with them.</summary>
    public MatchRules HostRules { get; private set; } = MatchRules.Default;

    /// <summary>How well practice bots fight, as this player last picked in the pause menu.</summary>
    public BotDifficulty BotDifficulty { get; private set; } = Tuning.DefaultBotDifficulty;

    private bool _unsaved;

    public override void _EnterTree()
    {
        Instance = this;
        var config = new ConfigFile();
        config.Load(FilePath); // no file yet: everything stays at its default
        Current = Read(config);
        HostRules = ReadRules(config);
        BotDifficulty = ReadBotDifficulty(config);
        Apply(Current);
    }

    /// <summary>Quitting with the settings screen still open keeps the changes.</summary>
    public override void _ExitTree() => Save();

    /// <summary>Changes the settings and applies them right away. <see cref="Save"/> writes them.</summary>
    public void Change(GameSettings settings)
    {
        settings = settings.Normalized();
        if (settings == Current)
        {
            return;
        }
        Current = settings;
        Apply(settings);
        _unsaved = true;
    }

    /// <summary>Remembers the rules picked as a host. <see cref="Save"/> writes them.</summary>
    public void ChangeHostRules(MatchRules rules)
    {
        rules = rules.Normalized();
        if (rules != HostRules)
        {
            HostRules = rules;
            _unsaved = true;
        }
    }

    /// <summary>Remembers the practice bots' skill. <see cref="Save"/> writes it.</summary>
    public void ChangeBotDifficulty(BotDifficulty difficulty)
    {
        if (difficulty != BotDifficulty)
        {
            BotDifficulty = difficulty;
            _unsaved = true;
        }
    }

    /// <summary>Writes any changes to the file, keeping what else it holds.</summary>
    public void Save()
    {
        if (!_unsaved)
        {
            return;
        }
        var config = new ConfigFile();
        config.Load(FilePath);
        Write(config, Current);
        WriteRules(config, HostRules);
        WriteBotDifficulty(config, BotDifficulty);
        Error error = config.Save(FilePath);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Couldn't save settings to {FilePath}: {error}");
        }
        _unsaved = false;
    }

    /// <summary>Reads settings from a settings file; anything missing or broken falls back to its default.</summary>
    public static GameSettings Read(ConfigFile config)
    {
        GameSettings fallback = GameSettings.Default;
        return new GameSettings(
            ReadInt(config, "video", "max_fps", fallback.MaxFps),
            ReadInt(config, "audio", "volume", fallback.VolumePercent),
            ReadInt(config, "video", "hurt_shake", fallback.HurtShakePercent),
            ReadInt(config, "controls", "mouse_sensitivity", fallback.SensitivityPercent),
            ReadInt(config, "video", "fov", fallback.Fov)).Normalized();
    }

    public static void Write(ConfigFile config, GameSettings settings)
    {
        config.SetValue("video", "max_fps", settings.MaxFps);
        config.SetValue("audio", "volume", settings.VolumePercent);
        config.SetValue("video", "hurt_shake", settings.HurtShakePercent);
        config.SetValue("controls", "mouse_sensitivity", settings.SensitivityPercent);
        config.SetValue("video", "fov", settings.Fov);
    }

    public static MatchRules ReadRules(ConfigFile config) => new MatchRules(
        ReadNumber(config, "rules", "strafe_speed", Tuning.DefaultStrafeSpeed),
        ReadNumber(config, "rules", "strafe_grip", Tuning.DefaultStrafeGrip),
        ReadNumber(config, "rules", "wasted_click_delay", Tuning.DefaultWastedClickDelay),
        ReadNumber(config, "rules", "pushing", Tuning.DefaultPushing),
        ReadNumber(config, "rules", "combo_knockback", Tuning.DefaultComboKnockback)).Normalized();

    public static void WriteRules(ConfigFile config, MatchRules rules)
    {
        config.SetValue("rules", "strafe_speed", rules.StrafeSpeed);
        config.SetValue("rules", "strafe_grip", rules.StrafeGrip);
        config.SetValue("rules", "wasted_click_delay", rules.WastedClickDelay);
        config.SetValue("rules", "pushing", rules.Pushing);
        config.SetValue("rules", "combo_knockback", rules.ComboKnockback);
    }

    /// <summary>The saved practice-bot skill ("easy", "normal" or "hard"); anything else is the default.</summary>
    public static BotDifficulty ReadBotDifficulty(ConfigFile config)
    {
        Variant value = config.GetValue("practice", "bot_skill", "");
        return value.VariantType == Variant.Type.String
            && System.Enum.TryParse(value.AsString(), ignoreCase: true, out BotDifficulty difficulty)
            && System.Enum.IsDefined(difficulty)
            ? difficulty
            : Tuning.DefaultBotDifficulty;
    }

    public static void WriteBotDifficulty(ConfigFile config, BotDifficulty difficulty) =>
        config.SetValue("practice", "bot_skill", BotSkill.Label(difficulty).ToLowerInvariant());

    private static double ReadNumber(ConfigFile config, string section, string key, double fallback)
    {
        Variant value = config.GetValue(section, key, fallback);
        return value.VariantType is Variant.Type.Float or Variant.Type.Int ? value.AsDouble() : fallback;
    }

    private static int ReadInt(ConfigFile config, string section, string key, int fallback)
    {
        Variant value = config.GetValue(section, key, fallback);
        return value.VariantType switch
        {
            Variant.Type.Int => (int)System.Math.Clamp(value.AsInt64(), int.MinValue, int.MaxValue),
            Variant.Type.Float when double.IsFinite(value.AsDouble()) && System.Math.Abs(value.AsDouble()) < int.MaxValue =>
                (int)System.Math.Round(value.AsDouble()),
            _ => fallback,
        };
    }

    private static void Apply(GameSettings settings)
    {
        Engine.MaxFps = settings.MaxFps;
        int master = AudioServer.GetBusIndex("Master");
        AudioServer.SetBusMute(master, settings.VolumePercent <= 0);
        AudioServer.SetBusVolumeDb(master, settings.VolumePercent <= 0 ? -80f : (float)settings.VolumeDb);
    }
}
