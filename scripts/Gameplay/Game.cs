using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.Net;
using UhcPvp.Ui;
using UhcPvp.World;

namespace UhcPvp.Gameplay;

/// <summary>
/// The match: builds the arena, spawns players (people and practice bots) and keeps everyone in
/// sync. Combat lives in Game.Combat.cs, the fishing rod in Game.Rod.cs.
///
/// Networking model (docs/netcode.md): each player's game owns its own movement and sends its
/// state to the host 60 times a second; the host runs the bots. The host sends everyone a
/// snapshot of all players 60 times a second, and each game draws the other players slightly in
/// the past so they move smoothly.
/// </summary>
public partial class Game : Node3D
{
    private readonly Dictionary<int, Player> _players = new();
    /// <summary>Host only: latest state of every player.</summary>
    private readonly Dictionary<int, (double Time, PlayerState State)> _latestStates = new();
    /// <summary>Test stats: where we've seen each other player. Kept after they leave.</summary>
    private readonly Dictionary<int, Aabb> _seenBounds = new();
    private (Vec3d Home, float Yaw)? _nextBotHome;

    public VoxelWorld World { get; private set; } = null!;
    public Player LocalPlayer { get; private set; } = null!;
    public int PlayerCount => _players.Count;
    public IEnumerable<Player> Players => _players.Values;
    /// <summary>How practice bots behave (host only; changed from the pause menu).</summary>
    public BotMode PracticeBotMode { get; private set; } = BotMode.Still;
    /// <summary>How well practice bots fight (host only; changed from the pause menu and remembered).</summary>
    public BotDifficulty PracticeBotDifficulty { get; private set; } = Tuning.DefaultBotDifficulty;

    /// <summary>Kill messages and other notices for the HUD.</summary>
    public event Action<string>? Notice;

    private static Network Net => Network.Instance;
    private static Session Session => Session.Instance;

    public override void _Ready()
    {
        World = Arena.Build();
        var renderer = new WorldRenderer { Name = "World" };
        AddChild(renderer);
        renderer.Build(World);
        SetupEnvironment();

        var args = LaunchArgs.Instance;
        PracticeBotMode = args.DummyMode;
        PracticeBotDifficulty = args.BotDifficulty ?? Settings.Instance.BotDifficulty;
        LocalPlayer = SpawnPlayer(Net.MyId);
        if (args.BotMode is BotMode mode)
        {
            LocalPlayer.Brain = new BotBrain(mode, Net.MyId, args.BotDifficulty ?? Tuning.DefaultBotDifficulty);
        }
        foreach (int id in Session.Players.Keys.Where(id => id != Net.MyId))
        {
            OnPlayerAdded(id);
        }

        AddChild(new Hud { Name = "Hud", Game = this });

        Session.PlayerAdded += OnPlayerAdded;
        Session.PlayerRemoved += OnPlayerRemoved;
        Net.MessageReceived += OnMessage;
        args.Quitting += PrintTestReport;
        args.ScheduleScreenshots();
        if (Net.IsHost)
        {
            int dummies = args.Dummies + (Session.IsPractice ? 1 : 0);
            for (int i = 0; i < dummies; i++)
            {
                AddPracticeBot();
            }
        }
        GD.Print($"[game] match started as {LocalPlayer.PlayerName} (id {Net.MyId}, team {LocalPlayer.Team.DisplayName()})");
    }

    public override void _ExitTree()
    {
        Session.PlayerAdded -= OnPlayerAdded;
        Session.PlayerRemoved -= OnPlayerRemoved;
        Net.MessageReceived -= OnMessage;
        LaunchArgs.Instance.Quitting -= PrintTestReport;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Net.IsOnline)
        {
            return;
        }
        double now = Net.HostTime;
        StepRods(now);
        if (Net.IsHost)
        {
            foreach (Player player in _players.Values.Where(p => p.IsOwned))
            {
                _latestStates[player.PeerId] = (now, player.GetState());
            }
            BroadcastSnapshot();
        }
        else
        {
            Net.SendToHost(new StateUpdate(now, LocalPlayer.GetState()), reliable: false);
        }
    }

    public override void _Process(double delta)
    {
        double now = Net.HostTime;
        foreach (Player player in _players.Values)
        {
            if (!player.IsOwned)
            {
                player.UpdateRemote(now, delta);
            }
            if (player != LocalPlayer)
            {
                TrackSeen(player);
            }
        }
    }

    public Player? Find(int id) => _players.GetValueOrDefault(id);

    /// <summary>Living players on the other team (whom bots go after).</summary>
    public IReadOnlyList<Player> EnemiesOf(Player player) =>
        _players.Values.Where(p => p != player && !p.IsDead && p.Team != player.Team).ToList();

    /// <summary>
    /// The push on one of our own players (us or a bot) from everyone overlapping it, teammates
    /// too (the pushing twist; see <see cref="Pushing"/>). Others are where this game sees them:
    /// our bots where they stand, remote players where they're drawn.
    /// </summary>
    public Vec3d PushOn(Player player, double strength)
    {
        if (strength <= 0 || player.Motor is null)
        {
            return Vec3d.Zero;
        }
        Vec3d push = Vec3d.Zero;
        foreach (Player other in _players.Values)
        {
            if (other != player && !other.IsDead)
            {
                push += Pushing.Away(player.Feet, other.IsOwned ? other.Feet : other.ShownFeet, strength, player.Motor.OnGround);
            }
        }
        return push;
    }

    /// <summary>Host only: adds a practice bot on the other team, a few blocks in front of you, facing you.</summary>
    public void AddPracticeBot()
    {
        if (!Net.IsHost)
        {
            return;
        }
        Team team = LocalPlayer.Team == Team.Red ? Team.Blue : Team.Red;
        int existing = _players.Values.Count(p => p.Kind == PlayerKind.Bot);
        Vec3d forward = Vec3d.Forward(LocalPlayer.Yaw);
        Vec3d side = Vec3d.Left(LocalPlayer.Yaw) * ((existing % 3 - 1) * 2.0);
        Vec3d home = LocalPlayer.Feet + forward * Tuning.PracticeDummyDistance + side;
        Vector3I size = Arena.WorldSize;
        home = new Vec3d(
            Math.Clamp(home.X, 1.5, size.X - 1.5), Tuning.ArenaFloorY, Math.Clamp(home.Z, 1.5, size.Z - 1.5));
        float facing = Mathf.Wrap(LocalPlayer.Yaw + 180f, -180f, 180f);
        _nextBotHome = (home, facing);
        if (Session.AddBot(team) == 0)
        {
            Notice?.Invoke("The match is full");
        }
        _nextBotHome = null;
    }

    /// <summary>Host only: removes every practice bot.</summary>
    public void RemovePracticeBots()
    {
        foreach (int id in _players.Values.Where(p => p.Kind == PlayerKind.Bot).Select(p => p.PeerId).ToList())
        {
            Session.RemoveBot(id);
        }
    }

    public void SetPracticeBotMode(BotMode mode)
    {
        PracticeBotMode = mode;
        foreach (Player bot in _players.Values.Where(p => p.Kind == PlayerKind.Bot && p.Brain is not null))
        {
            bot.Brain!.Mode = mode;
        }
    }

    /// <summary>Host only: how well every practice bot fights, now and in later matches.</summary>
    public void SetPracticeBotDifficulty(BotDifficulty difficulty)
    {
        PracticeBotDifficulty = difficulty;
        Settings.Instance.ChangeBotDifficulty(difficulty);
        foreach (Player bot in _players.Values.Where(p => p.Kind == PlayerKind.Bot && p.Brain is not null))
        {
            bot.Brain!.Difficulty = difficulty;
        }
    }

    private Player SpawnPlayer(int id)
    {
        PlayerKind kind = id == Net.MyId ? PlayerKind.Local
            : Session.IsBot(id) && Net.IsHost ? PlayerKind.Bot
            : PlayerKind.Remote;
        Team team = Session.TeamOf(id);
        Vec3d home = Vec3d.From(Arena.SpawnPosition(team, Session.SlotOf(id)));
        float yaw = Arena.SpawnYaw(team);
        if (kind == PlayerKind.Bot && _nextBotHome is { } placed)
        {
            (home, yaw) = placed;
        }
        var player = new Player();
        player.Setup(this, id, kind, World, home, yaw);
        if (kind == PlayerKind.Bot)
        {
            player.Brain = new BotBrain(PracticeBotMode, id, PracticeBotDifficulty);
        }
        AddChild(player);
        _players[id] = player;
        return player;
    }

    private void OnPlayerAdded(int id)
    {
        if (!_players.ContainsKey(id))
        {
            SpawnPlayer(id);
        }
    }

    private void OnPlayerRemoved(int id)
    {
        if (_players.Remove(id, out Player? player))
        {
            player.QueueFree();
        }
        _latestStates.Remove(id);
        RemoveBobber(id);
    }

    private void OnMessage(int from, IMessage message)
    {
        switch (message)
        {
            case StateUpdate update when Net.IsHost:
                HostReceiveState(from, update);
                break;
            case Snapshot snapshot when !Net.IsHost:
                ReceiveSnapshot(snapshot);
                break;
            case Attack attack:
                OnAttackMessage(from, attack);
                break;
            case Hurt hurt:
                OnHurtMessage(from, hurt);
                break;
            case Swing swing:
                OnSwingMessage(from, swing);
                break;
            case RodCast cast:
                OnRodCastMessage(from, cast);
                break;
            case RodHook hook:
                OnRodHookMessage(from, hook);
                break;
        }
    }

    private void HostReceiveState(int from, StateUpdate update)
    {
        if (!_players.TryGetValue(from, out Player? player) || player.IsOwned || !update.State.IsValid
            || !double.IsFinite(update.Time))
        {
            return;
        }
        double now = Net.HostTime;
        // A timestamp from the future means the sender's clock is still settling.
        double time = update.Time > now + 1.0 ? now : update.Time;
        _latestStates[from] = (time, update.State);
        player.ReceiveState(time, update.State, now);
    }

    private void BroadcastSnapshot()
    {
        var entries = _latestStates.Select(kv => new SnapshotEntry(kv.Key, kv.Value.Time, kv.Value.State)).ToArray();
        Net.Broadcast(new Snapshot(entries), reliable: false);
    }

    private void ReceiveSnapshot(Snapshot snapshot)
    {
        double now = Net.HostTime;
        foreach (SnapshotEntry entry in snapshot.Players)
        {
            if (_players.TryGetValue(entry.Id, out Player? player) && !player.IsOwned && entry.State.IsValid
                && double.IsFinite(entry.Time))
            {
                player.ReceiveState(entry.Time, entry.State, now);
            }
        }
    }

    private void SetupEnvironment()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.53f, 0.72f, 1.0f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.55f,
            },
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55, -30, 0),
            LightEnergy = 0.75f,
        });
    }

    private void TrackSeen(Player player)
    {
        var point = new Aabb(player.Position, Vector3.Zero);
        _seenBounds[player.PeerId] = _seenBounds.TryGetValue(player.PeerId, out Aabb bounds)
            ? bounds.Merge(point)
            : point;
    }

    /// <summary>Printed when --quit-after ends the game; tools/smoke_test.sh checks this line.</summary>
    private void PrintTestReport()
    {
        int moving = _seenBounds.Values.Count(b => b.Size.Length() > 1.0f);
        GD.Print($"SMOKE name={LocalPlayer.PlayerName} host={Net.IsHost} players_now={PlayerCount} "
            + $"seen={_seenBounds.Count} moving={moving} landed={HitsLanded} rods={RodHitsLanded} taken={HitsTaken} "
            + $"kills={Kills} ping_ms={(int)Math.Round(Net.PingSeconds * 1000)} "
            + $"strafe={MatchRules.Percent(Session.Rules.StrafeSpeed)} grip={MatchRules.Percent(Session.Rules.StrafeGrip)} "
            + $"wasted={Session.Rules.WastedClickDelay:0.00}");
    }
}
