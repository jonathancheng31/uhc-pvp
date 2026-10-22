using System;
using System.Collections.Generic;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;
using UhcPvp.Gameplay;

namespace UhcPvp.Ui;

/// <summary>
/// In-match overlay: crosshair, hearts, armor and hotbar, hit readout (combo and reach), kill
/// feed, death screen, debug info (F3) and the pause menu (Esc) with practice-bot controls.
/// </summary>
public partial class Hud : CanvasLayer
{
    /// <summary>Pixel art is drawn at this multiple of its size, like Minecraft's GUI scale 2.</summary>
    private const int GuiScale = 2;
    private const double ReadoutSeconds = 2.5;
    private const double NoticeSeconds = 5.0;
    private const string HudDir = "res://assets/textures/hud/";

    private readonly List<TextureRect> _hearts = new();
    private readonly List<TextureRect> _armor = new();
    private readonly List<(Label Label, double ShownAt)> _notices = new();
    private Texture2D _heartFull = null!, _heartHalf = null!, _heartEmpty = null!;
    private Texture2D _armorFull = null!, _armorHalf = null!, _armorEmpty = null!;
    private Label _debugLabel = null!;
    private Label _readout = null!;
    private Label _clickHint = null!;
    private Label _practiceHint = null!;
    private Control _pauseMenu = null!;
    private Control _pauseBox = null!;
    private SettingsMenu _settings = null!;
    private Control _deathScreen = null!;
    private Label _respawnLabel = null!;
    private VBoxContainer _noticeBox = null!;
    private OptionButton? _botMode;
    private ColorRect _rodCooldown = null!;

    public Game? Game { get; set; }

    private static bool Headless => DisplayServer.GetName() == "headless";

    public override void _Ready()
    {
        LoadIcons();
        BuildCrosshair();
        BuildReadout();
        BuildBottomBar();
        BuildNoticeBox();
        BuildDeathScreen();
        _debugLabel = new Label { Position = new Vector2(8, 6) };
        AddShadow(_debugLabel);
        AddChild(_debugLabel);
        _practiceHint = MakeCornerHint();
        BuildPauseMenu();
        _clickHint = new Label { Text = "Click to play", HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _clickHint.SetAnchorsPreset(Control.LayoutPreset.Center);
        _clickHint.Position += new Vector2(0, 40);
        _clickHint.AddThemeFontSizeOverride("font_size", 22);
        AddShadow(_clickHint);
        AddChild(_clickHint);
        if (Game is not null)
        {
            Game.Notice += ShowNotice;
        }
        _settings.Closed += CloseSettings;
        if (!LaunchArgs.Instance.Bot)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        if (LaunchArgs.Instance.OpenSettings)
        {
            SetPaused(true);
            OpenSettings();
        }
    }

    public override void _ExitTree()
    {
        if (Game is not null)
        {
            Game.Notice -= ShowNotice;
        }
        _settings.Closed -= CloseSettings;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(Controls.Pause))
        {
            // Esc in the settings goes back to the pause menu, like Minecraft's screens.
            if (_settings.Visible)
            {
                _settings.Close();
            }
            else
            {
                SetPaused(!_pauseMenu.Visible);
            }
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(Controls.ToggleDebug))
        {
            _debugLabel.Visible = !_debugLabel.Visible;
        }
        else if (@event is InputEventMouseButton { Pressed: true } && !_pauseMenu.Visible
            && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled(); // the click that grabs the mouse shouldn't also attack
        }
    }

    public override void _Process(double delta)
    {
        _clickHint.Visible = !_pauseMenu.Visible && !LaunchArgs.Instance.Bot && !Headless
            && Input.MouseMode != Input.MouseModeEnum.Captured;
        if (Game is null)
        {
            return;
        }
        Player me = Game.LocalPlayer;
        UpdateBars(me);
        UpdateRodCooldown(me);
        UpdateReadout();
        UpdateNotices();
        _deathScreen.Visible = me.IsDead;
        if (me.IsDead)
        {
            _respawnLabel.Text = $"Respawning in {Math.Max(Math.Ceiling(me.RespawnIn), 1):0}...";
        }
        _practiceHint.Visible = Network.Instance.IsHost && HasBots() && !_pauseMenu.Visible;
        if (_debugLabel.Visible)
        {
            UpdateDebug(me);
        }
    }

    private bool HasBots()
    {
        foreach (Player p in Game!.Players)
        {
            if (p.Kind == PlayerKind.Bot)
            {
                return true;
            }
        }
        return Session.Instance.IsPractice;
    }

    private void UpdateBars(Player me)
    {
        // Each heart is 2 health; each armor icon is 2 armor points.
        double health = Math.Ceiling(me.Health);
        for (int i = 0; i < _hearts.Count; i++)
        {
            double left = health - i * 2;
            _hearts[i].Texture = left >= 2 ? _heartFull : left >= 1 ? _heartHalf : _heartEmpty;
        }
        for (int i = 0; i < _armor.Count; i++)
        {
            int left = Tuning.KitArmorPoints - i * 2;
            _armor[i].Texture = left >= 2 ? _armorFull : left >= 1 ? _armorHalf : _armorEmpty;
        }
    }

    /// <summary>
    /// A pale cover over the rod while it cools down, sinking out of the slot as it gets ready
    /// (whole GUI pixels, like the rest of the pixel art).
    /// </summary>
    private void UpdateRodCooldown(Player me)
    {
        int height = (int)Math.Ceiling(RodIconSize * me.RodCooldown.FractionLeft);
        _rodCooldown.Visible = height > 0;
        _rodCooldown.Position = new Vector2(3, 3 + RodIconSize - height) * GuiScale;
        _rodCooldown.Size = new Vector2(RodIconSize, height) * GuiScale;
    }

    private void UpdateReadout()
    {
        double since = Network.Instance.Now - Game!.LastHitAt;
        if (since > ReadoutSeconds || Game.Combo == 0)
        {
            _readout.Text = "";
            return;
        }
        _readout.Text = $"{Game.Combo} hit{(Game.Combo == 1 ? "" : "s")}  ·  {Game.LastReach:0.00} blocks";
        _readout.Modulate = new Color(1, 1, 1, (float)Math.Clamp((ReadoutSeconds - since) / 0.5, 0, 1));
    }

    private void ShowNotice(string text)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Right };
        AddShadow(label);
        _noticeBox.AddChild(label);
        _notices.Add((label, Network.Instance.Now));
        if (_notices.Count > 5)
        {
            _notices[0].Label.QueueFree();
            _notices.RemoveAt(0);
        }
    }

    private void UpdateNotices()
    {
        double now = Network.Instance.Now;
        for (int i = _notices.Count - 1; i >= 0; i--)
        {
            double age = now - _notices[i].ShownAt;
            if (age > NoticeSeconds)
            {
                _notices[i].Label.QueueFree();
                _notices.RemoveAt(i);
            }
            else
            {
                _notices[i].Label.Modulate = new Color(1, 1, 1, (float)Math.Clamp((NoticeSeconds - age) / 1.0, 0, 1));
            }
        }
    }

    private void UpdateDebug(Player me)
    {
        var net = Network.Instance;
        var lines = new List<string>
        {
            $"UHC PvP {ProjectSettings.GetSetting("application/config/version").AsString()}",
            $"FPS: {Engine.GetFramesPerSecond():0}",
            $"Role: {(net.IsHost ? "host" : "player")}",
            $"Ping: {net.PingSeconds * 1000:0} ms",
            $"Players: {Game!.PlayerCount}/{Tuning.MaxPlayers}",
            $"Team: {me.Team.DisplayName()}",
            $"XYZ: {me.Position.X:0.00} / {me.Position.Y:0.00} / {me.Position.Z:0.00}",
            $"Facing: yaw {me.Yaw:0.0}, pitch {me.Pitch:0.0}",
            $"Health: {me.Health:0.##}",
        };
        if (me.Motor is { } motor)
        {
            var states = new List<string> { motor.OnGround ? "on ground" : "in air" };
            if (motor.Sprinting)
            {
                states.Add("sprinting");
            }
            if (motor.Sneaking)
            {
                states.Add("sneaking");
            }
            lines.Add($"Speed: {motor.HorizontalSpeed:0.00} blocks/s ({string.Join(", ", states)})");
            lines.Add($"Sprint knockback ready: {(motor.ServerSprinting ? "yes" : "no (w-tap)")}");
        }
        lines.Add($"Hits landed: {Game.HitsLanded}, taken: {Game.HitsTaken}");
        if (net.IsLagSimulated)
        {
            lines.Add($"Simulated lag: +{LaunchArgs.Instance.PingMs:0} ms ping");
        }
        _debugLabel.Text = string.Join('\n', lines);
    }

    private void SetPaused(bool paused)
    {
        // The match keeps running while the menu is open; this only frees the mouse.
        _pauseMenu.Visible = paused;
        CloseSettings();
        if (_botMode is not null && Game is not null)
        {
            _botMode.Selected = _botMode.GetItemIndex((int)Game.PracticeBotMode);
        }
        Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    private void OpenSettings()
    {
        _pauseBox.Visible = false;
        _settings.Refresh();
        _settings.Visible = true;
    }

    private void CloseSettings()
    {
        _settings.Visible = false;
        _pauseBox.Visible = true;
    }

    private void LoadIcons()
    {
        _heartFull = GD.Load<Texture2D>(HudDir + "heart_full.png");
        _heartHalf = GD.Load<Texture2D>(HudDir + "heart_half.png");
        _heartEmpty = GD.Load<Texture2D>(HudDir + "heart_empty.png");
        _armorFull = GD.Load<Texture2D>(HudDir + "armor_full.png");
        _armorHalf = GD.Load<Texture2D>(HudDir + "armor_half.png");
        _armorEmpty = GD.Load<Texture2D>(HudDir + "armor_empty.png");
    }

    private void BuildCrosshair()
    {
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);
        var cross = new Control { CustomMinimumSize = new Vector2(18, 18), MouseFilter = Control.MouseFilterEnum.Ignore };
        center.AddChild(cross);
        foreach (Rect2 rect in new[] { new Rect2(8, 0, 2, 18), new Rect2(0, 8, 18, 2) })
        {
            cross.AddChild(new ColorRect
            {
                Color = new Color(1, 1, 1, 0.85f),
                Position = rect.Position,
                Size = rect.Size,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
    }

    private void BuildReadout()
    {
        // Centered just under the crosshair.
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 70), MouseFilter = Control.MouseFilterEnum.Ignore });
        _readout = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(320, 0) };
        AddShadow(_readout);
        column.AddChild(_readout);
        center.AddChild(column);
    }

    /// <summary>
    /// Armor, hearts and the hotbar along the bottom middle, like Minecraft's, plus a slot to
    /// the left for the fishing rod in your other hand (right-click).
    /// </summary>
    private void BuildBottomBar()
    {
        var bar = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 2);
        bar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        bar.GrowHorizontal = Control.GrowDirection.Both;
        bar.GrowVertical = Control.GrowDirection.Begin;
        bar.Position += new Vector2(0, -8);
        AddChild(bar);

        // Armor and hearts line up with the hotbar's first slot, past the rod's slot.
        bar.AddChild(Indented(IconRow(_armor, _armorFull), 22 + OffHandGap));
        bar.AddChild(Indented(IconRow(_hearts, _heartFull), 22 + OffHandGap));

        var hotbar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hotbar.AddThemeConstantOverride("separation", 0);
        var slotTexture = GD.Load<Texture2D>(HudDir + "slot.png");
        var selected = GD.Load<Texture2D>(HudDir + "slot_selected.png");
        var sword = GD.Load<Texture2D>(HudDir + "sword.png");
        var rod = GD.Load<Texture2D>(HudDir + "rod.png");
        // The rod's slot hangs off the left; an equal gap on the right keeps the hotbar centered.
        var rodSlot = Slot(slotTexture);
        rodSlot.AddChild(Icon(rod, new Vector2(3, 3) * GuiScale, new Vector2(RodIconSize, RodIconSize) * GuiScale));
        _rodCooldown = new ColorRect
        {
            Color = new Color(1, 1, 1, 0.5f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        rodSlot.AddChild(_rodCooldown);
        hotbar.AddChild(rodSlot);
        hotbar.AddChild(Spacer(OffHandGap));
        for (int i = 0; i < 9; i++)
        {
            var slot = Slot(slotTexture);
            if (i == 0)
            {
                slot.AddChild(Icon(sword, new Vector2(3, 3) * GuiScale, new Vector2(16, 16) * GuiScale));
                slot.AddChild(Icon(selected, new Vector2(-1, -1) * GuiScale, new Vector2(24, 24) * GuiScale));
            }
            hotbar.AddChild(slot);
        }
        hotbar.AddChild(Spacer(OffHandGap + 22));
        bar.AddChild(hotbar);
    }

    /// <summary>Gap between the rod's slot and the hotbar, in GUI pixels.</summary>
    private const int OffHandGap = 6;
    /// <summary>Size of the rod's icon in its slot, in GUI pixels.</summary>
    private const int RodIconSize = 16;

    private static TextureRect Slot(Texture2D texture) => new()
    {
        Texture = texture,
        CustomMinimumSize = new Vector2(22, 22) * GuiScale,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
    };

    private static MarginContainer Indented(Control content, int left)
    {
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", left * GuiScale);
        margin.AddChild(content);
        return margin;
    }

    private static Control Spacer(int width) =>
        new() { CustomMinimumSize = new Vector2(width, 0) * GuiScale, MouseFilter = Control.MouseFilterEnum.Ignore };

    private static HBoxContainer IconRow(List<TextureRect> into, Texture2D texture)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", -1 * GuiScale);
        for (int i = 0; i < 10; i++)
        {
            var icon = new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(9, 9) * GuiScale,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            into.Add(icon);
            row.AddChild(icon);
        }
        return row;
    }

    private static TextureRect Icon(Texture2D texture, Vector2 position, Vector2 size) => new()
    {
        Texture = texture,
        Position = position,
        Size = size,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private void BuildNoticeBox()
    {
        _noticeBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _noticeBox.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _noticeBox.GrowHorizontal = Control.GrowDirection.Begin;
        _noticeBox.Position += new Vector2(-10, 8);
        AddChild(_noticeBox);
    }

    private void BuildDeathScreen()
    {
        _deathScreen = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _deathScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var tint = new ColorRect { Color = new Color(0.6f, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Ignore };
        tint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _deathScreen.AddChild(tint);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _deathScreen.AddChild(center);
        var box = new VBoxContainer();
        center.AddChild(box);
        var title = new Label { Text = "You died!", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 48);
        AddShadow(title);
        box.AddChild(title);
        _respawnLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        AddShadow(_respawnLabel);
        box.AddChild(_respawnLabel);
        AddChild(_deathScreen);
    }

    private Label MakeCornerHint()
    {
        var label = new Label { Text = "Practice: press Esc for bot options", Modulate = new Color(1, 1, 1, 0.7f) };
        label.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        label.GrowVertical = Control.GrowDirection.Begin;
        label.Position += new Vector2(10, -10);
        AddShadow(label);
        AddChild(label);
        return label;
    }

    private void BuildPauseMenu()
    {
        _pauseMenu = new Control { Visible = false };
        _pauseMenu.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _pauseMenu.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _pauseMenu.AddChild(center);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", SettingsMenu.PanelStyle());
        center.AddChild(panel);
        _pauseBox = panel;
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        _settings = new SettingsMenu { Visible = false };
        center.AddChild(_settings);
        var title = new Label { Text = "Paused", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);
        box.AddChild(MenuButton("Back to game", () => SetPaused(false)));
        box.AddChild(MenuButton("Settings", OpenSettings));

        if (Network.Instance.IsHost && Game is not null)
        {
            box.AddChild(new HSeparator());
            box.AddChild(new Label { Text = "Practice bots", HorizontalAlignment = HorizontalAlignment.Center });
            box.AddChild(MenuButton("Add bot", () => Game.AddPracticeBot()));
            box.AddChild(MenuButton("Remove all bots", () => Game.RemovePracticeBots()));
            _botMode = new OptionButton { CustomMinimumSize = new Vector2(260, 40) };
            _botMode.AddItem("Bots stand still", (int)BotMode.Still);
            _botMode.AddItem("Bots strafe", (int)BotMode.Strafe);
            _botMode.AddItem("Bots fight back", (int)BotMode.Fight);
            _botMode.AddItem("Bots chase you (no hitting)", (int)BotMode.Chase);
            _botMode.Selected = _botMode.GetItemIndex((int)Game.PracticeBotMode);
            _botMode.ItemSelected += index => Game.SetPracticeBotMode((BotMode)_botMode.GetItemId((int)index));
            box.AddChild(_botMode);
            box.AddChild(new HSeparator());
        }
        box.AddChild(MenuButton("Leave match", () => Session.Instance.Leave()));
        AddChild(_pauseMenu);
    }

    private static Button MenuButton(string text, Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(260, 40) };
        button.Pressed += onPressed;
        return button;
    }

    private static void AddShadow(Label label)
    {
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
    }
}
