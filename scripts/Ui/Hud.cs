using System.Collections.Generic;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;
using UhcPvp.Gameplay;

namespace UhcPvp.Ui;

/// <summary>In-match overlay: crosshair, debug info (F3) and the pause menu (Esc).</summary>
public partial class Hud : CanvasLayer
{
    private Label _debugLabel = null!;
    private Control _pauseMenu = null!;
    private Label _clickHint = null!;

    public Game? Game { get; set; }

    private static bool Headless => DisplayServer.GetName() == "headless";

    public override void _Ready()
    {
        BuildCrosshair();
        _debugLabel = new Label { Position = new Vector2(8, 6) };
        AddShadow(_debugLabel);
        AddChild(_debugLabel);
        BuildPauseMenu();
        _clickHint = new Label
        {
            Text = "Click to play",
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        _clickHint.SetAnchorsPreset(Control.LayoutPreset.Center);
        _clickHint.Position += new Vector2(0, 40);
        _clickHint.AddThemeFontSizeOverride("font_size", 22);
        AddShadow(_clickHint);
        AddChild(_clickHint);
        if (!LaunchArgs.Instance.Bot)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(Controls.Pause))
        {
            SetPaused(!_pauseMenu.Visible);
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
        }
    }

    public override void _Process(double delta)
    {
        _clickHint.Visible = !_pauseMenu.Visible && !LaunchArgs.Instance.Bot && !Headless
            && Input.MouseMode != Input.MouseModeEnum.Captured;
        if (!_debugLabel.Visible || Game is null)
        {
            return;
        }
        var net = Network.Instance;
        Player player = Game.LocalPlayer;
        var lines = new List<string>
        {
            $"UHC PvP {ProjectSettings.GetSetting("application/config/version").AsString()}",
            $"FPS: {Engine.GetFramesPerSecond():0}",
            $"Role: {(net.IsHost ? "host" : "player")}",
            $"Ping: {net.PingSeconds * 1000:0} ms",
            $"Players: {Game.PlayerCount}/{Tuning.MaxPlayers}",
            $"Team: {player.Team.DisplayName()}",
            $"XYZ: {player.Position.X:0.00} / {player.Position.Y:0.00} / {player.Position.Z:0.00}",
            $"Facing: yaw {player.Yaw:0.0}, pitch {player.Pitch:0.0}",
        };
        if (player.Motor is { } motor)
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
        }
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
        Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
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
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        center.AddChild(box);
        var title = new Label { Text = "Paused", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);
        var resume = new Button { Text = "Back to game", CustomMinimumSize = new Vector2(240, 40) };
        resume.Pressed += () => SetPaused(false);
        box.AddChild(resume);
        var leave = new Button { Text = "Leave match", CustomMinimumSize = new Vector2(240, 40) };
        leave.Pressed += () => Session.Instance.Leave();
        box.AddChild(leave);
        AddChild(_pauseMenu);
    }

    private static void AddShadow(Label label)
    {
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
    }
}
