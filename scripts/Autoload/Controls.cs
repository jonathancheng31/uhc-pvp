using System.Collections.Generic;
using Godot;

namespace UhcPvp.Autoload;

/// <summary>
/// Registers the game's input actions in code, so key bindings live in one readable place and
/// can be made rebindable later. Movement keys use physical key positions, so WASD works on
/// any keyboard layout.
/// </summary>
public partial class Controls : Node
{
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Jump = "jump";
    public const string Sneak = "sneak";
    public const string Attack = "attack";
    public const string Use = "use";
    public const string Hotbar1 = "hotbar_1";
    public const string Hotbar2 = "hotbar_2";
    public const string ToggleDebug = "toggle_debug";
    public const string Pause = "pause";

    private static readonly Dictionary<string, Key> KeyBindings = new()
    {
        [MoveForward] = Key.W,
        [MoveBack] = Key.S,
        [MoveLeft] = Key.A,
        [MoveRight] = Key.D,
        [Jump] = Key.Space,
        [Sneak] = Key.Shift,
        [Hotbar1] = Key.Key1,
        [Hotbar2] = Key.Key2,
        [ToggleDebug] = Key.F3,
        [Pause] = Key.Escape,
    };

    private static readonly Dictionary<string, MouseButton> MouseBindings = new()
    {
        [Attack] = MouseButton.Left,
        [Use] = MouseButton.Right,
    };

    public override void _EnterTree()
    {
        foreach (var (action, key) in KeyBindings)
        {
            Reset(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
        foreach (var (action, button) in MouseBindings)
        {
            Reset(action);
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
        }
    }

    private static void Reset(string action)
    {
        if (InputMap.HasAction(action))
        {
            InputMap.ActionEraseEvents(action);
        }
        else
        {
            InputMap.AddAction(action);
        }
    }
}
