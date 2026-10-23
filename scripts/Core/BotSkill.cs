namespace UhcPvp.Core;

/// <summary>How well the fighting practice bots play (pause menu, "Bot skill").</summary>
public enum BotDifficulty
{
    Easy,
    Normal,
    Hard,
}

/// <summary>
/// Everything that makes one fighting bot better than another. The bot plays like a person:
/// it sees you a moment late, its aim trails and wanders, it clicks at a human speed, and it
/// knows (or doesn't) the techniques good 1.8 players use, each as a chance it uses it. The
/// numbers for each difficulty are in <see cref="Tuning"/> (<see cref="Tuning.EasyBot"/>,
/// <see cref="Tuning.NormalBot"/>, <see cref="Tuning.HardBot"/>); docs/combat.md explains them.
/// </summary>
/// <param name="ReactionSeconds">
/// How old what the bot sees is: it aims and decides from where you were this long ago. Tracking
/// a moving target takes people about 0.1 to 0.2 seconds.
/// </param>
/// <param name="Anticipation">
/// How much of that delay it makes up by leading your movement: 0 aims where you were, 1 where
/// you'll be if you keep moving the same way. A change of direction fools it either way.
/// </param>
/// <param name="AimErrorDegrees">How far its aim wanders off your chest, typically, in degrees.</param>
/// <param name="AimSettleSeconds">How quickly its aim catches up with where it wants to look (most of the gap closes in this time).</param>
/// <param name="TurnSpeed">The fastest it turns, in degrees per second.</param>
/// <param name="HitJoltDegrees">How far a hit on the bot throws its aim off, typically, in degrees.</param>
/// <param name="ClicksPerSecond">Average clicks per second when it clicks freely.</param>
/// <param name="ClickSpread">How irregular its clicking is: the spread of the gaps between clicks, as a fraction of the average.</param>
/// <param name="ReachSlack">
/// How far beyond the 3-block reach it already clicks when its crosshair is on you, in blocks.
/// At 0 it times its first click for the moment you come into reach, which wins the race for
/// the first hit; more is spam clicking as you come in, which lands at whatever moment of its
/// clicking rhythm you happen to arrive.
/// </param>
/// <param name="Patience">
/// Chance that, after landing a hit, it waits for your hit protection to end before clicking
/// again (clicking into it would slow it down) and keeps just out of your reach meanwhile,
/// instead of running into you.
/// </param>
/// <param name="TimingErrorSeconds">
/// How far off its timing is, typically, in seconds: of the end of your hit protection, and of
/// a jump reset.
/// </param>
/// <param name="WTapChance">Chance it w-taps after a sprint hit, so its next hit knocks you back fully.</param>
/// <param name="StrafeMinSeconds">Shortest time it strafes one way before it may switch.</param>
/// <param name="StrafeMaxSeconds">Longest time it strafes one way.</param>
/// <param name="RodChance">Chance it takes each good chance to throw its rod.</param>
/// <param name="RodTactics">Whether it rods to extend its combos and to escape yours, not only to open a fight.</param>
/// <param name="HitSelectChance">
/// Chance that, when your sword hit lands on it, it clicks you right back while you're still in
/// reach. Clicking someone while sprinting slows you to 60% (1.8.9), knockback included, so this
/// takes more than half the knockback away and keeps it close enough to hit back: hit selecting.
/// Without it, the bot doesn't click while your knockback carries it, so your combos land in full.
/// </param>
/// <param name="HoldForwardChance">
/// Chance it keeps holding W toward you while your knockback carries it. Steering against the
/// knockback in the air cuts the flight by about a third, so it's back in reach sooner; without
/// it, the bot lets the knockback carry it.
/// </param>
/// <param name="JumpResetChance">
/// Chance it tries to jump as your hit lands while it's on the ground. Only a jump on the very
/// Minecraft tick the knockback arrives takes some of it away (the jump pushes it back toward
/// you); how often it gets that tick depends on <paramref name="TimingErrorSeconds"/>.
/// </param>
/// <param name="EscapeChance">Chance it strafes off to one side after your hit, out of the line of your combo.</param>
public sealed record BotSkill(
    double ReactionSeconds,
    double Anticipation,
    double AimErrorDegrees,
    double AimSettleSeconds,
    double TurnSpeed,
    double HitJoltDegrees,
    double ClicksPerSecond,
    double ClickSpread,
    double ReachSlack,
    double Patience,
    double TimingErrorSeconds,
    double WTapChance,
    double StrafeMinSeconds,
    double StrafeMaxSeconds,
    double RodChance,
    bool RodTactics,
    double HitSelectChance,
    double HoldForwardChance,
    double JumpResetChance,
    double EscapeChance)
{
    public static BotSkill For(BotDifficulty difficulty) => difficulty switch
    {
        BotDifficulty.Easy => Tuning.EasyBot,
        BotDifficulty.Hard => Tuning.HardBot,
        _ => Tuning.NormalBot,
    };

    /// <summary>"Easy", "Normal" or "Hard".</summary>
    public static string Label(BotDifficulty difficulty) => difficulty switch
    {
        BotDifficulty.Easy => "Easy",
        BotDifficulty.Hard => "Hard",
        _ => "Normal",
    };
}
