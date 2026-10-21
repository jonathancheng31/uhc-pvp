using System.Globalization;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;

namespace UhcPvp.Ui;

/// <summary>Title screen: pick a name, then host a match or join one by address.</summary>
public partial class MainMenu : Control
{
    private const string SettingsPath = "user://settings.cfg";

    private LineEdit _nameEdit = null!;
    private LineEdit _addressEdit = null!;
    private Label _status = null!;

    private static Session Session => Session.Instance;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        BuildUi();
        LoadSettings();
        _status.Text = Session.Status;
        if (LaunchArgs.Instance.WantsAutoStart)
        {
            Callable.From(AutoStart).CallDeferred();
        }
    }

    public override void _Process(double delta)
    {
        if (_status.Text != Session.Status)
        {
            _status.Text = Session.Status;
        }
    }

    private static void AutoStart()
    {
        var args = LaunchArgs.Instance;
        string name = args.PlayerName.Length > 0 ? args.PlayerName : "Bot";
        if (args.Host)
        {
            Session.Host(name, args.Port);
        }
        else
        {
            Session.Join(name, args.JoinAddress, args.Port, retries: 20);
        }
    }

    private void OnHostPressed()
    {
        SaveSettings();
        Session.Host(_nameEdit.Text, Tuning.DefaultPort);
    }

    private void OnJoinPressed()
    {
        SaveSettings();
        string address = _addressEdit.Text.Trim();
        int port = Tuning.DefaultPort;
        int colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            port = parsed;
            address = address[..colon];
        }
        if (address.Length == 0)
        {
            _status.Text = "Type the host's address first";
            return;
        }
        if (Session.Join(_nameEdit.Text, address, port) != Error.Ok)
        {
            _status.Text = "Couldn't start connecting";
        }
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0.11f, 0.13f, 0.17f) };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        box.AddThemeConstantOverride("separation", 10);
        center.AddChild(box);

        var title = new Label { Text = "UHC PvP", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 48);
        box.AddChild(title);
        box.AddChild(new Label
        {
            Text = $"dev build {ProjectSettings.GetSetting("application/config/version").AsString()}",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0.5f),
        });

        box.AddChild(Caption("Name"));
        _nameEdit = new LineEdit { MaxLength = Session.MaxNameLength, PlaceholderText = "Your name" };
        box.AddChild(_nameEdit);
        var hostButton = new Button { Text = "Host match", CustomMinimumSize = new Vector2(0, 40) };
        hostButton.Pressed += OnHostPressed;
        box.AddChild(hostButton);

        box.AddChild(Caption("Host address"));
        _addressEdit = new LineEdit { PlaceholderText = "e.g. 192.168.1.20" };
        _addressEdit.TextSubmitted += _ => OnJoinPressed();
        box.AddChild(_addressEdit);
        var joinButton = new Button { Text = "Join match", CustomMinimumSize = new Vector2(0, 40) };
        joinButton.Pressed += OnJoinPressed;
        box.AddChild(joinButton);

        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1, 0.85f, 0.5f),
        };
        box.AddChild(_status);

        var quitButton = new Button { Text = "Quit" };
        quitButton.Pressed += () => GetTree().Quit();
        box.AddChild(quitButton);
    }

    private static Label Caption(string text) => new() { Text = text, Modulate = new Color(1, 1, 1, 0.7f) };

    private void LoadSettings()
    {
        var config = new ConfigFile();
        config.Load(SettingsPath);
        _nameEdit.Text = config.GetValue("player", "name", "").AsString();
        _addressEdit.Text = config.GetValue("player", "last_address", "127.0.0.1").AsString();
    }

    private void SaveSettings()
    {
        var config = new ConfigFile();
        config.Load(SettingsPath);
        config.SetValue("player", "name", _nameEdit.Text);
        config.SetValue("player", "last_address", _addressEdit.Text);
        config.Save(SettingsPath);
    }
}
