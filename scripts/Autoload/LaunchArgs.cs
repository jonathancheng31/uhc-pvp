using System;
using System.Globalization;
using Godot;
using UhcPvp.Core;
using UhcPvp.Gameplay;

namespace UhcPvp.Autoload;

/// <summary>
/// Command-line options, mainly for automated tests and quick local testing. Pass them after
/// "--", for example:
///   godot --path . -- --host --name=Alice --bot
///   godot --path . -- --join=127.0.0.1 --ping=120 --jitter=20 --loss=2
///
/// --host               Host a match right away.
/// --join=ADDRESS       Join a match right away.
/// --port=N             Port to host on or join (default Tuning.DefaultPort).
/// --name=NAME          Player name.
/// --bot[=MODE]         Play by itself. MODE is wander (default: run, hop, sneak) or fight.
/// --practice           Host a solo practice match with a training dummy.
/// --dummies=N          Host only: add N practice bots.
/// --dummy-mode=MODE    How practice bots behave: still (default), strafe or fight.
/// --ping=MS            Simulate this much extra ping.
/// --jitter=MS          Simulate this much random extra delay.
/// --loss=PERCENT       Simulate this percentage of lost packets.
/// --quit-after=SECONDS Quit after this many seconds.
/// --screenshot=PATH    Save a screenshot of the match (needs a real display).
/// --screenshot-at=SEC  Seconds into the match to take the screenshot (default 2).
/// </summary>
public partial class LaunchArgs : Node
{
    public static LaunchArgs Instance { get; private set; } = null!;

    /// <summary>Raised just before --quit-after closes the game, so tests can print results.</summary>
    public event Action? Quitting;

    public bool Host { get; private set; }
    public string JoinAddress { get; private set; } = "";
    public int Port { get; private set; } = Tuning.DefaultPort;
    public string PlayerName { get; private set; } = "";
    /// <summary>If set, the local player is played by a bot brain in this mode.</summary>
    public BotMode? BotMode { get; private set; }
    public bool Bot => BotMode is not null;
    public bool Practice { get; private set; }
    public int Dummies { get; private set; }
    public BotMode DummyMode { get; private set; } = Gameplay.BotMode.Still;
    public double PingMs { get; private set; }
    public double JitterMs { get; private set; }
    public double LossPercent { get; private set; }
    public double QuitAfter { get; private set; }
    public string ScreenshotPath { get; private set; } = "";
    public double ScreenshotAt { get; private set; } = 2.0;

    public bool WantsAutoStart => Host || Practice || JoinAddress.Length > 0;

    public override void _EnterTree()
    {
        Instance = this;
        Parse(OS.GetCmdlineUserArgs());
    }

    public override void _Ready()
    {
        if (QuitAfter > 0.0)
        {
            GetTree().CreateTimer(QuitAfter).Timeout += () =>
            {
                Quitting?.Invoke();
                GetTree().Quit();
            };
        }
    }

    public void Parse(string[] args)
    {
        foreach (string arg in args)
        {
            int eq = arg.IndexOf('=', StringComparison.Ordinal);
            string key = eq >= 0 ? arg[..eq] : arg;
            string value = eq >= 0 ? arg[(eq + 1)..] : "";
            switch (key)
            {
                case "--host": Host = true; break;
                case "--join": JoinAddress = value; break;
                case "--port": Port = ParseInt(value, Tuning.DefaultPort); break;
                case "--name": PlayerName = value; break;
                case "--bot": BotMode = ParseMode(value, Gameplay.BotMode.Wander); break;
                case "--practice": Practice = true; break;
                case "--dummies": Dummies = ParseInt(value, 0); break;
                case "--dummy-mode": DummyMode = ParseMode(value, Gameplay.BotMode.Still); break;
                case "--ping": PingMs = ParseDouble(value); break;
                case "--jitter": JitterMs = ParseDouble(value); break;
                case "--loss": LossPercent = ParseDouble(value); break;
                case "--quit-after": QuitAfter = ParseDouble(value); break;
                case "--screenshot": ScreenshotPath = value; break;
                case "--screenshot-at": ScreenshotAt = ParseDouble(value); break;
                default: GD.PushWarning($"Unknown launch option: {arg}"); break;
            }
        }
    }

    private static BotMode ParseMode(string value, BotMode fallback) =>
        Enum.TryParse(value, ignoreCase: true, out BotMode mode) ? mode : fallback;

    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : 0.0;

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;
}
