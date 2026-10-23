using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
/// --dummy-mode=MODE    How practice bots behave: still (default), strafe, fight or chase.
/// --bot-skill=LEVEL    How well fighting bots (and --bot=fight) play: easy, normal or hard
///                      (default: the host's last choice from the pause menu; normal for --bot).
/// --strafe=PERCENT     Host only: twists for this match, overriding the host's last choice
/// --grip=PERCENT         from the pause menu: strafe speed (100 is 1.8), strafe snappiness
/// --wasted-clicks=SEC    (0 is 1.8), the wasted-click delay in seconds (0 is 1.8) and
/// --push=PERCENT         player pushing (0 is 1.8, 100 is solid).
/// --hold=KEYS          Hold movement keys all match: any of w, a, s, d, and j to jump (tests,
///                      screenshots: --hold=w on two opposite players runs them into each other).
/// --ping=MS            Simulate this much extra ping.
/// --jitter=MS          Simulate this much random extra delay.
/// --loss=PERCENT       Simulate this percentage of lost packets.
/// --quit-after=SECONDS Quit after this many seconds.
/// --settings           Open the settings screen: on the main menu, or over the pause menu in a match.
/// --pause              Open the pause menu when the match starts (for screenshots).
/// --twists             Open the Twists screen when the match starts (for screenshots).
/// --screenshot=PATH    Save a screenshot (needs a real display), of the match or, without
///                      --host/--join/--practice, of the main menu.
/// --screenshot-at=SEC  Seconds into the match (or after the menu opens) to take the screenshot
///                      (default 2). A comma-separated list takes several (1,1.1,1.2 saves
///                      PATH_1.png, PATH_2.png, PATH_3.png).
/// --record-audio=PATH  Record everything the game plays to a WAV file, saved when it quits
///                      (check it with tools/gen_sounds.py --analyze PATH).
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
    /// <summary>--bot-skill, or null to use the host's saved choice.</summary>
    public BotDifficulty? BotDifficulty { get; private set; }
    /// <summary>--strafe as a multiplier (1.3 for 130), or null to use the host's saved choice.</summary>
    public double? StrafeSpeed { get; private set; }
    /// <summary>--grip as a fraction (0.5 for 50), or null.</summary>
    public double? StrafeGrip { get; private set; }
    /// <summary>--wasted-clicks in seconds, or null.</summary>
    public double? WastedClickDelay { get; private set; }
    /// <summary>--push as a fraction (1 for 100), or null.</summary>
    public double? Pushing { get; private set; }
    /// <summary>--hold: movement keys the local player holds all match (w, a, s, d, j), or empty.</summary>
    public string HoldKeys { get; private set; } = "";

    /// <summary>The host's rules: its saved choice, with any twist given on the command line instead.</summary>
    public MatchRules RulesFor(MatchRules saved) => new MatchRules(
        StrafeSpeed ?? saved.StrafeSpeed, StrafeGrip ?? saved.StrafeGrip, WastedClickDelay ?? saved.WastedClickDelay,
        Pushing ?? saved.Pushing).Normalized();
    public double PingMs { get; private set; }
    public double JitterMs { get; private set; }
    public double LossPercent { get; private set; }
    public double QuitAfter { get; private set; }
    public bool OpenSettings { get; private set; }
    public bool OpenPause { get; private set; }
    public bool OpenTwists { get; private set; }
    public string ScreenshotPath { get; private set; } = "";
    public string RecordAudioPath { get; private set; } = "";
    public IReadOnlyList<double> ScreenshotTimes { get; private set; } = DefaultScreenshotTimes;

    private static readonly double[] DefaultScreenshotTimes = { 2.0 };

    public bool WantsAutoStart => Host || Practice || JoinAddress.Length > 0;

    public override void _EnterTree()
    {
        Instance = this;
        Parse(OS.GetCmdlineUserArgs());
    }

    public override void _Ready()
    {
        if (RecordAudioPath.Length > 0)
        {
            AddChild(new AudioRecorder { FilePath = RecordAudioPath });
        }
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
                case "--bot-skill": BotDifficulty = ParseDifficulty(value); break;
                case "--strafe": StrafeSpeed = ParseDouble(value) / 100.0; break;
                case "--grip": StrafeGrip = ParseDouble(value) / 100.0; break;
                case "--wasted-clicks": WastedClickDelay = ParseDouble(value); break;
                case "--push": Pushing = ParseDouble(value) / 100.0; break;
                case "--hold": HoldKeys = value.ToLowerInvariant(); break;
                case "--ping": PingMs = ParseDouble(value); break;
                case "--jitter": JitterMs = ParseDouble(value); break;
                case "--loss": LossPercent = ParseDouble(value); break;
                case "--quit-after": QuitAfter = ParseDouble(value); break;
                case "--settings": OpenSettings = true; break;
                case "--pause": OpenPause = true; break;
                case "--twists": OpenTwists = true; break;
                case "--screenshot": ScreenshotPath = value; break;
                case "--screenshot-at": ScreenshotTimes = ParseTimes(value); break;
                case "--record-audio": RecordAudioPath = value; break;
                case "--filter": break; // the test runner's option (tests/TestRunner.cs)
                default: GD.PushWarning($"Unknown launch option: {arg}"); break;
            }
        }
    }

    /// <summary>"1, 2.5,3" becomes [1, 2.5, 3]. Bad or negative entries are skipped; none left means the default.</summary>
    public static double[] ParseTimes(string value)
    {
        var times = new List<double>();
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t >= 0)
            {
                times.Add(t);
            }
        }
        return times.Count > 0 ? times.ToArray() : DefaultScreenshotTimes;
    }

    /// <summary>Takes the --screenshot shots, timed from now (the match starting, or the main menu opening).</summary>
    public void ScheduleScreenshots()
    {
        IReadOnlyList<double> times = ScreenshotTimes;
        for (int i = 0; i < times.Count && ScreenshotPath.Length > 0; i++)
        {
            string path = times.Count == 1 ? ScreenshotPath : NumberedPath(ScreenshotPath, i + 1);
            GetTree().CreateTimer(times[i]).Timeout += () => TakeScreenshot(path);
        }
    }

    private async void TakeScreenshot(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[screenshot] saved to {path} ({err})");
    }

    /// <summary>"/tmp/shot.png" and 2 become "/tmp/shot_2.png".</summary>
    public static string NumberedPath(string path, int number) =>
        Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)}_{number}{Path.GetExtension(path)}");

    private static BotMode ParseMode(string value, BotMode fallback) =>
        Enum.TryParse(value, ignoreCase: true, out BotMode mode) ? mode : fallback;

    private static BotDifficulty? ParseDifficulty(string value) =>
        Enum.TryParse(value, ignoreCase: true, out BotDifficulty difficulty) && Enum.IsDefined(difficulty) ? difficulty : null;

    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : 0.0;

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;
}
