using System;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;

namespace UhcPvp.Ui;

/// <summary>
/// The Twists screen, opened from the pause menu: our changes to 1.8.9's mechanics, for the
/// whole match (<see cref="MatchRules"/>). The host moves the sliders and every player gets the
/// change at once; everyone else sees the host's choice. Two columns, movement and fighting, so
/// it fits a 720p screen. Leftmost is plain 1.8, except the head start, where 1.8's is marked;
/// a value at its default (<see cref="MatchRules.Default"/>) is marked too.
/// </summary>
public partial class TwistsMenu : PanelContainer
{
    /// <summary>Wide enough that the descriptions wrap to two or three lines, so four twists in a column still fit 720p.</summary>
    private const float ColumnWidth = 520;

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
    private HSlider _rodCooldown = null!;
    private Label _rodCooldownValue = null!;
    private Button? _defaults;
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
            ? $"Our changes to Minecraft 1.8, for everyone in this match. All the way left is plain 1.8, except the head start: 1.8's is {HeadStart.Label(Tuning.McHeadStart)}."
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
        (_rodCooldown, _rodCooldownValue) = Twist(fighting, "Rod cooldown", 0.0, Tuning.MaxRodCooldown, Tuning.RodCooldownStep,
            "After a throw, the rod can't be thrown again for this long. A bar under your crosshair shows it. Off lets every right-click throw.");
        _strafeSpeed.ValueChanged += value => Change(Session.Rules with { StrafeSpeed = value });
        _strafeGrip.ValueChanged += value => Change(Session.Rules with { StrafeGrip = value });
        _pushing.ValueChanged += value => Change(Session.Rules with { Pushing = value });
        _comboKnockback.ValueChanged += value => Change(Session.Rules with { ComboKnockback = 1.0 - value });
        _headStart.ValueChanged += value => Change(Session.Rules with { HeadStart = value });
        _wastedClicks.ValueChanged += value => Change(Session.Rules with { WastedClickDelay = value });
        _rodCooldown.ValueChanged += value => Change(Session.Rules with { RodCooldown = value });

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        box.AddChild(buttons);
        if (IsHost)
        {
            _defaults = AddButton(buttons, "Reset to defaults", () => Session.SetRules(MatchRules.Default));
        }
        AddButton(buttons, "Done", Close);

        Session.RulesChanged += Refresh;
        Refresh();
    }

    public override void _ExitTree() => Session.RulesChanged -= Refresh;

    /// <summary>Shows the rules as they are now.</summary>
    public void Refresh()
    {
        _refreshing = true;
        MatchRules rules = Session.Rules;
        MatchRules byDefault = MatchRules.Default;
        MatchRules minecraft = MatchRules.Minecraft;
        _strafeSpeed.Value = rules.StrafeSpeed;
        _strafeSpeedValue.Text = Marked(MatchRules.Percent(rules.StrafeSpeed), rules.StrafeSpeed == byDefault.StrafeSpeed);
        _strafeGrip.Value = rules.StrafeGrip;
        _strafeGripValue.Text = Marked(MatchRules.PercentOrOff(rules.StrafeGrip), rules.StrafeGrip == byDefault.StrafeGrip);
        _pushing.Value = rules.Pushing;
        _pushingValue.Text = Marked(MatchRules.PercentOrOff(rules.Pushing), rules.Pushing == byDefault.Pushing);
        _comboKnockback.Value = 1.0 - rules.ComboKnockback;
        _comboKnockbackValue.Text = Marked(MatchRules.Percent(rules.ComboKnockback), rules.ComboKnockback == byDefault.ComboKnockback);
        _headStart.Value = rules.HeadStart;
        // The one slider where plain 1.8 isn't all the way left, so its 1.8 point is marked too.
        _headStartValue.Text = Marked(HeadStart.Label(rules.HeadStart), rules.HeadStart == byDefault.HeadStart,
            rules.HeadStart == minecraft.HeadStart);
        _wastedClicks.Value = rules.WastedClickDelay;
        _wastedClicksValue.Text = Marked(MatchRules.Seconds(rules.WastedClickDelay), rules.WastedClickDelay == byDefault.WastedClickDelay);
        _rodCooldown.Value = rules.RodCooldown;
        _rodCooldownValue.Text = Marked(MatchRules.Seconds(rules.RodCooldown), rules.RodCooldown == byDefault.RodCooldown);
        if (_defaults is not null)
        {
            _defaults.Disabled = rules.IsDefault;
        }
        _refreshing = false;
    }

    /// <summary>A twist's value, with "(default)" when it's the default and "(1.8)" when asked to mark 1.8's.</summary>
    private static string Marked(string shown, bool isDefault, bool isMinecraft = false) => (isDefault, isMinecraft) switch
    {
        (true, true) => $"{shown} (default, 1.8)",
        (true, false) => $"{shown} (default)",
        (false, true) => $"{shown} (1.8)",
        _ => shown,
    };

    /// <summary>Saves the host's choice for its next match and closes the screen.</summary>
    public void Close()
    {
        Settings.Instance.Save();
        Closed?.Invoke();
    }

    private void Change(MatchRules rules)
    {
        if (!_refreshing)
        {
            Session.SetRules(rules);
        }
    }

    /// <summary>A button across its share of the row.</summary>
    private static Button AddButton(HBoxContainer row, string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += pressed;
        row.AddChild(button);
        return button;
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
