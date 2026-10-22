using Godot;
using UhcPvp.Core;

namespace UhcPvp.Autoload;

/// <summary>
/// The player's settings (max FPS and volume, see <see cref="GameSettings"/>): loaded from
/// user://settings.cfg at startup and applied to the engine. The settings screen
/// (scripts/Ui/SettingsMenu.cs) changes them, which applies at once, and saves when it closes.
/// The same file also keeps the name and address typed in the main menu.
/// </summary>
public partial class Settings : Node
{
    public const string FilePath = "user://settings.cfg";

    public static Settings Instance { get; private set; } = null!;

    public GameSettings Current { get; private set; } = GameSettings.Default;

    private bool _unsaved;

    public override void _EnterTree()
    {
        Instance = this;
        var config = new ConfigFile();
        config.Load(FilePath); // no file yet: everything stays at its default
        Current = Read(config);
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
            ReadInt(config, "audio", "volume", fallback.VolumePercent)).Normalized();
    }

    public static void Write(ConfigFile config, GameSettings settings)
    {
        config.SetValue("video", "max_fps", settings.MaxFps);
        config.SetValue("audio", "volume", settings.VolumePercent);
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
