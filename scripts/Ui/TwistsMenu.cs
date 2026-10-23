using System;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;

namespace UhcPvp.Ui;

/// <summary>
/// The Twists screen, opened from the pause menu: our changes to 1.8.9's mechanics, for the
/// whole match (<see cref="MatchRules"/>). The host moves the sliders and every player gets the
/// change at once; everyone else sees the host's choice. Leftmost is always plain 1.8.
/// </summary>
public partial class TwistsMenu : PanelContainer
{
    private HSlider _strafeSpeed = null!;
    private Label _strafeSpeedValue = null!;
    private HSlider _strafeGrip = null!;
    private Label _strafeGripValue = null!;
    private HSlider _wastedClicks = null!;
    private Label _wastedClicksValue = null!;
    private HSlider _pushing = null!;
    private Label _pushingValue = null!;
    private bool _refreshing;

    /// <summary>Raised when the screen closes (Done or Esc); the pause menu shows again.</summary>
    public event Action? Closed;

    private static Session Session => Session.Instance;
    private static bool IsHost => Network.Instance.IsHost;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", SettingsMenu.PanelStyle());
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);

        var title = new Label { Text = "Twists", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);
        box.AddChild(Note(IsHost
            ? "Our changes to Minecraft 1.8, for everyone in this match. All the way left is plain 1.8."
            : "Our changes to Minecraft 1.8, set by the host for everyone in this match."));

        (_strafeSpeed, _strafeSpeedValue) = Twist(box, "Strafe speed", 1.0, Tuning.MaxStrafeSpeed, Tuning.StrafeSpeedStep,
            "A and D move you faster sideways on the ground. Never faster than a straight sprint.");
        (_strafeGrip, _strafeGripValue) = Twist(box, "Strafe snappiness", 0.0, Tuning.MaxStrafeGrip, Tuning.StrafeGripStep,
            "Starting, stopping and switching between A and D happen sooner on the ground (1.8 takes a quarter second to switch).");
        (_wastedClicks, _wastedClicksValue) = Twist(box, "Wasted clicks", 0.0, Tuning.MaxWastedClickDelay, Tuning.WastedClickDelayStep,
            "A click that does nothing (a miss, or someone still in hit protection) blocks your clicks this long. Your crosshair turns red while it does.");
        (_pushing, _pushingValue) = Twist(box, "Player pushing", 0.0, Tuning.MaxPushing, Tuning.PushingStep,
            "Players can't walk through each other: you stop at someone and shove them along. Lower is softer; below 60% you can push through.");
        _strafeSpeed.ValueChanged += value => Change(Session.Rules with { StrafeSpeed = value });
        _strafeGrip.ValueChanged += value => Change(Session.Rules with { StrafeGrip = value });
        _wastedClicks.ValueChanged += value => Change(Session.Rules with { WastedClickDelay = value });
        _pushing.ValueChanged += value => Change(Session.Rules with { Pushing = value });

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        if (IsHost)
        {
            var reset = new Button { Text = "Reset to plain 1.8", CustomMinimumSize = new Vector2(0, 36) };
            reset.Pressed += () => Session.SetRules(MatchRules.Default);
            box.AddChild(reset);
        }
        var done = new Button { Text = "Done", CustomMinimumSize = new Vector2(0, 40) };
        done.Pressed += Close;
        box.AddChild(done);

        Session.RulesChanged += Refresh;
        Refresh();
    }

    public override void _ExitTree() => Session.RulesChanged -= Refresh;

    /// <summary>Shows the rules as they are now.</summary>
    public void Refresh()
    {
        _refreshing = true;
        MatchRules rules = Session.Rules;
        _strafeSpeed.Value = rules.StrafeSpeed;
        _strafeSpeedValue.Text = MatchRules.Percent(rules.StrafeSpeed);
        _strafeGrip.Value = rules.StrafeGrip;
        _strafeGripValue.Text = rules.StrafeGrip <= 0 ? "Off" : MatchRules.Percent(rules.StrafeGrip);
        _wastedClicks.Value = rules.WastedClickDelay;
        _wastedClicksValue.Text = MatchRules.Seconds(rules.WastedClickDelay);
        _pushing.Value = rules.Pushing;
        _pushingValue.Text = rules.Pushing <= 0 ? "Off" : MatchRules.Percent(rules.Pushing);
        _refreshing = false;
    }

    /// <summary>Saves the host's choice for its next match and closes the screen.</summary>
    public void Close()
    {
        Settings.Instance.Save();
        Closed?.Invoke();
    }

    /// <summary>A short line for the pause menu: which twists are on.</summary>
    public static string Summary(MatchRules rules)
    {
        if (rules.IsMinecraft)
        {
            return "All off: plain 1.8";
        }
        var parts = new System.Collections.Generic.List<string>();
        if (rules.StrafeSpeed != Tuning.DefaultStrafeSpeed)
        {
            parts.Add($"strafe {MatchRules.Percent(rules.StrafeSpeed)}");
        }
        if (rules.StrafeGrip != Tuning.DefaultStrafeGrip)
        {
            parts.Add($"snappy {MatchRules.Percent(rules.StrafeGrip)}");
        }
        if (rules.WastedClickDelay != Tuning.DefaultWastedClickDelay)
        {
            parts.Add($"wasted clicks {MatchRules.Seconds(rules.WastedClickDelay)}");
        }
        if (rules.Pushing != Tuning.DefaultPushing)
        {
            parts.Add($"pushing {MatchRules.Percent(rules.Pushing)}");
        }
        return "On: " + string.Join(", ", parts);
    }

    private void Change(MatchRules rules)
    {
        if (!_refreshing)
        {
            Session.SetRules(rules);
        }
    }

    private static (HSlider Slider, Label Value) Twist(VBoxContainer box, string name, double min, double max, double step, string description)
    {
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(140, 0) });
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Editable = IsHost,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        row.AddChild(slider);
        var value = new Label { CustomMinimumSize = new Vector2(56, 0), HorizontalAlignment = HorizontalAlignment.Right };
        row.AddChild(value);
        box.AddChild(row);
        box.AddChild(Note(description));
        return (slider, value);
    }

    private static Label Note(string text) => new()
    {
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(400, 0),
        Modulate = new Color(1, 1, 1, 0.6f),
    };
}
