using System;
using System.Globalization;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;
using UhcPvp.Gameplay;

namespace UhcPvp.Ui;

/// <summary>
/// The settings screen, opened from the main menu and from the pause menu (Esc) in a match:
/// max FPS, volume, camera shake when hit, mouse sensitivity and field of view. Changes apply at
/// once and are saved when it closes.
/// </summary>
public partial class SettingsMenu : PanelContainer
{
    private OptionButton _maxFps = null!;
    private HSlider _volume = null!;
    private Label _volumeValue = null!;
    private HSlider _shake = null!;
    private Label _shakeValue = null!;
    private HSlider _sensitivity = null!;
    private Label _sensitivityValue = null!;
    private HSlider _fov = null!;
    private Label _fovValue = null!;
    private AudioStreamPlayer _preview = null!;
    private bool _refreshing;

    /// <summary>Raised when the screen closes (Done or Esc); whoever opened it shows their menu again.</summary>
    public event Action? Closed;

    private static Settings Settings => Settings.Instance;

    /// <summary>
    /// A dark backing for menus shown over the match (the same colour as the main menu's
    /// background, so it blends in there), so the game behind doesn't show through.
    /// </summary>
    public static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = new Color(0.11f, 0.13f, 0.17f, 0.97f),
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 20,
        ContentMarginRight = 20,
        ContentMarginTop = 16,
        ContentMarginBottom = 20,
    };

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", PanelStyle());
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);
        BuildControls(box);
        Refresh();
    }

    private void BuildControls(VBoxContainer box)
    {
        var title = new Label { Text = "Settings", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);

        box.AddChild(Caption("Max FPS"));
        _maxFps = new OptionButton { CustomMinimumSize = new Vector2(0, 40) };
        foreach (int fps in GameSettings.MaxFpsChoices)
        {
            _maxFps.AddItem(GameSettings.MaxFpsLabel(fps), fps);
        }
        _maxFps.ItemSelected += OnMaxFpsSelected;
        box.AddChild(_maxFps);

        box.AddChild(Caption("Volume"));
        var volumeRow = new HBoxContainer();
        volumeRow.AddThemeConstantOverride("separation", 12);
        _volume = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        _volume.ValueChanged += OnVolumeChanged;
        // Letting go of the slider plays a hit sound at the new volume.
        _volume.DragEnded += changed =>
        {
            if (changed)
            {
                _preview.Play();
            }
        };
        volumeRow.AddChild(_volume);
        _volumeValue = new Label { CustomMinimumSize = new Vector2(52, 0), HorizontalAlignment = HorizontalAlignment.Right };
        volumeRow.AddChild(_volumeValue);
        box.AddChild(volumeRow);

        _preview = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(Effects.HurtSoundPath) };
        AddChild(_preview);

        box.AddChild(Caption("Camera shake when hit (100% = Minecraft's)"));
        (_shake, _shakeValue) = SliderRow(box, 0, 100, 5);
        _shake.ValueChanged += OnShakeChanged;

        box.AddChild(Caption("Mouse sensitivity (same numbers as Minecraft's)"));
        (_sensitivity, _sensitivityValue) = SliderRow(box, 0, GameSettings.MaxSensitivityPercent, 1);
        _sensitivity.ValueChanged += value => OnSliderChanged(value, _sensitivityValue, SensitivityText,
            percent => Settings.Current with { SensitivityPercent = percent });

        box.AddChild(Caption("Field of view (70 = Minecraft's normal)"));
        (_fov, _fovValue) = SliderRow(box, Tuning.MinFov, Tuning.MaxFov, 1);
        _fov.ValueChanged += value => OnSliderChanged(value, _fovValue, FovText,
            fov => Settings.Current with { Fov = fov });

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var done = new Button { Text = "Done", CustomMinimumSize = new Vector2(0, 40) };
        done.Pressed += Close;
        box.AddChild(done);
    }

    /// <summary>Shows the settings as they are now (call when opening the screen).</summary>
    public void Refresh()
    {
        _refreshing = true;
        GameSettings current = Settings.Current;
        _maxFps.Selected = _maxFps.GetItemIndex(current.MaxFps);
        _volume.Value = current.VolumePercent;
        _volumeValue.Text = VolumeText(current.VolumePercent);
        _shake.Value = current.HurtShakePercent;
        _shakeValue.Text = VolumeText(current.HurtShakePercent);
        _sensitivity.Value = current.SensitivityPercent;
        _sensitivityValue.Text = SensitivityText(current.SensitivityPercent);
        _fov.Value = current.Fov;
        _fovValue.Text = FovText(current.Fov);
        _refreshing = false;
    }

    /// <summary>Saves the settings and closes the screen.</summary>
    public void Close()
    {
        Settings.Save();
        Closed?.Invoke();
    }

    private void OnMaxFpsSelected(long index)
    {
        if (!_refreshing)
        {
            Settings.Change(Settings.Current with { MaxFps = _maxFps.GetItemId((int)index) });
        }
    }

    private void OnVolumeChanged(double value)
    {
        int percent = (int)Math.Round(value);
        _volumeValue.Text = VolumeText(percent);
        if (!_refreshing)
        {
            Settings.Change(Settings.Current with { VolumePercent = percent });
        }
    }

    private void OnShakeChanged(double value)
    {
        int percent = (int)Math.Round(value);
        _shakeValue.Text = VolumeText(percent);
        if (!_refreshing)
        {
            Settings.Change(Settings.Current with { HurtShakePercent = percent });
        }
    }

    private void OnSliderChanged(double value, Label shown, Func<int, string> text, Func<int, GameSettings> change)
    {
        int number = (int)Math.Round(value);
        shown.Text = text(number);
        if (!_refreshing)
        {
            Settings.Change(change(number));
        }
    }

    /// <summary>A slider with its value shown on the right, added to <paramref name="box"/>.</summary>
    private static (HSlider Slider, Label Value) SliderRow(VBoxContainer box, double min, double max, double step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        row.AddChild(slider);
        var value = new Label { CustomMinimumSize = new Vector2(52, 0), HorizontalAlignment = HorizontalAlignment.Right };
        row.AddChild(value);
        box.AddChild(row);
        return (slider, value);
    }

    private static string VolumeText(int percent) =>
        percent == 0 ? "Off" : percent.ToString(CultureInfo.InvariantCulture) + "%";

    private static string SensitivityText(int percent) => percent.ToString(CultureInfo.InvariantCulture) + "%";

    private static string FovText(int fov) => fov.ToString(CultureInfo.InvariantCulture);

    private static Label Caption(string text) => new() { Text = text, Modulate = new Color(1, 1, 1, 0.7f) };
}
