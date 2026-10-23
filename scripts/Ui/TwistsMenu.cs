using System;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;

namespace UhcPvp.Ui;

/// <summary>
/// The Twists screen, opened from the pause menu: our changes to 1.8.9's mechanics, for the
/// whole match (<see cref="MatchRules"/>). The host moves the sliders and every player gets the
/// change at once; everyone else sees the host's choice. Two columns, movement and fighting, so
/// it fits a 720p screen. Leftmost is plain 1.8, except the head start, where 1.8's is marked.
/// </summary>
public partial class TwistsMenu : PanelContainer
{
    private const float ColumnWidth = 380;

    private HSlider _strafeSpeed = null!;
    private Label _strafeSpeedValue = null!;
    private HSlider _strafeGrip = null!;
    private Label _strafeGripValue = null!;
    private HSlider _wastedClicks = null!;
    private Label _wastedClicksValue = null!;
    private HSlider _pushing = null!;
    private Label _pushingValue = null!;
    /// <summary>Goes right as combo knockback goes down: its value is how much of 1.8.9's sprint push is taken off.</summary>
    private HSlider _comboKnockback = null!;
    private Label _comboKnockbackValue = null!;
    private HSlider _headStart = null!;
    private Label _headStartValue = null!;
    private bool _refreshing;

    /// <summary>Raised when the screen closes (Done or Esc); the pause menu shows again.</summary>
    public event Action? Closed;

    private static Session Session => Session.Instance;
    private static bool IsHost => Network.Instance.IsHost;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", SettingsMenu.PanelStyle());
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);

        var title = new Label { Text = "Twists", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);
        box.AddChild(Note(IsHost
            ? "Our changes to Minecraft 1.8, for everyone in this match. All the way left is plain 1.8, except the head start: 1.8's is 175 ms."
            : "Our changes to Minecraft 1.8, set by the host for everyone in this match.",
            2 * ColumnWidth + 32));

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 32);
        box.AddChild(columns);
        VBoxContainer movement = Column(columns, "Movement");
        VBoxContainer fighting = Column(columns, "Fighting");

        (_strafeSpeed, _strafeSpeedValue) = Twist(movement, "Strafe speed", 1.0, Tuning.MaxStrafeSpeed, Tuning.StrafeSpeedStep,
            "A and D move you faster sideways on the ground. Never faster than a straight sprint.");
        (_strafeGrip, _strafeGripValue) = Twist(movement, "Strafe snappiness", 0.0, Tuning.MaxStrafeGrip, Tuning.StrafeGripStep,
            "Starting, stopping and switching between A and D happen sooner on the ground (1.8 takes a quarter second to switch).");
        (_pushing, _pushingValue) = Twist(movement, "Player pushing", 0.0, Tuning.MaxPushing, Tuning.PushingStep,
            "Players can't walk through each other: you stop at someone and shove them along. Lower is softer; below 60% you can push through.");
        (_comboKnockback, _comboKnockbackValue) = Twist(fighting, "Combo knockback", 0.0, 1.0 - Tuning.MinComboKnockback, Tuning.ComboKnockbackStep,
            "A sprint hit's extra push. Less sends the victim less far, so you catch up sooner and combos run longer. The lift, plain hits and the rod stay 1.8's.");
        (_headStart, _headStartValue) = Twist(fighting, "Head start", 0.0, Tuning.MaxHeadStart, Tuning.HeadStartStep,
            "Everyone else is shown, and hit, this far in the past, the same at any ping, so whoever chases after a hit gets back in reach first.");
        (_wastedClicks, _wastedClicksValue) = Twist(fighting, "Wasted clicks", 0.0, Tuning.MaxWastedClickDelay, Tuning.WastedClickDelayStep,
            "A click that does nothing (a miss, or someone still in hit protection) blocks your clicks this long. Your crosshair turns red while it does.");
        _strafeSpeed.ValueChanged += value => Change(Session.Rules with { StrafeSpeed = value });
        _strafeGrip.ValueChanged += value => Change(Session.Rules with { StrafeGrip = value });
        _pushing.ValueChanged += value => Change(Session.Rules with { Pushing = value });
        _comboKnockback.ValueChanged += value => Change(Session.Rules with { ComboKnockback = 1.0 - value });
        _headStart.ValueChanged += value => Change(Session.Rules with { HeadStart = value });
        _wastedClicks.ValueChanged += value => Change(Session.Rules with { WastedClickDelay = value });

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        box.AddChild(buttons);
        if (IsHost)
        {
            var reset = new Button { Text = "Reset to plain 1.8", CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            reset.Pressed += () => Session.SetRules(MatchRules.Default);
            buttons.AddChild(reset);
        }
        var done = new Button { Text = "Done", CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        done.Pressed += Close;
        buttons.AddChild(done);

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
        _pushing.Value = rules.Pushing;
        _pushingValue.Text = rules.Pushing <= 0 ? "Off" : MatchRules.Percent(rules.Pushing);
        _comboKnockback.Value = 1.0 - rules.ComboKnockback;
        _comboKnockbackValue.Text = MatchRules.Percent(rules.ComboKnockback);
        _headStart.Value = rules.HeadStart;
        _headStartValue.Text = rules.HeadStart == Tuning.DefaultHeadStart
            ? $"{HeadStart.Label(rules.HeadStart)} (1.8)"
            : HeadStart.Label(rules.HeadStart);
        _wastedClicks.Value = rules.WastedClickDelay;
        _wastedClicksValue.Text = MatchRules.Seconds(rules.WastedClickDelay);
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
            parts.Add(Part("strafe", MatchRules.Percent(rules.StrafeSpeed)));
        }
        if (rules.StrafeGrip != Tuning.DefaultStrafeGrip)
        {
            parts.Add(Part("snappy", MatchRules.Percent(rules.StrafeGrip)));
        }
        if (rules.WastedClickDelay != Tuning.DefaultWastedClickDelay)
        {
            parts.Add(Part("wasted clicks", MatchRules.Seconds(rules.WastedClickDelay)));
        }
        if (rules.Pushing != Tuning.DefaultPushing)
        {
            parts.Add(Part("pushing", MatchRules.Percent(rules.Pushing)));
        }
        if (rules.ComboKnockback != Tuning.DefaultComboKnockback)
        {
            parts.Add(Part("combo knockback", MatchRules.Percent(rules.ComboKnockback)));
        }
        if (rules.HeadStart != Tuning.DefaultHeadStart)
        {
            parts.Add(Part("head start", HeadStart.Label(rules.HeadStart).ToLowerInvariant()));
        }
        return "On: " + string.Join(", ", parts);
    }

    /// <summary>One twist in the summary, kept on one line when the summary wraps (no-break spaces).</summary>
    private static string Part(string name, string value) => $"{name} {value}".Replace(' ', ' ');

    private void Change(MatchRules rules)
    {
        if (!_refreshing)
        {
            Session.SetRules(rules);
        }
    }

    /// <summary>A column of twists under a heading.</summary>
    private static VBoxContainer Column(HBoxContainer columns, string heading)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(ColumnWidth, 0) };
        column.AddThemeConstantOverride("separation", 4);
        var label = new Label { Text = heading };
        label.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(label);
        columns.AddChild(column);
        return column;
    }

    /// <summary>One twist: its name and value on a line, the slider across the column, and what it does.</summary>
    private static (HSlider Slider, Label Value) Twist(VBoxContainer column, string name, double min, double max, double step, string description)
    {
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var value = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        row.AddChild(value);
        column.AddChild(row);
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Editable = IsHost,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        column.AddChild(slider);
        column.AddChild(Note(description, ColumnWidth));
        return (slider, value);
    }

    private static Label Note(string text, float width) => new()
    {
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(width, 0),
        Modulate = new Color(1, 1, 1, 0.6f),
    };
}
