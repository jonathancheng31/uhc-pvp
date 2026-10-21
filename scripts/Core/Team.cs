using Godot;

namespace UhcPvp.Core;

public enum Team : byte
{
    Red,
    Blue,
}

public static class Teams
{
    public static string DisplayName(this Team team) => team == Team.Red ? "Red" : "Blue";

    public static Color Color(this Team team) =>
        team == Team.Red ? new Color(0.86f, 0.22f, 0.22f) : new Color(0.23f, 0.45f, 0.93f);
}
