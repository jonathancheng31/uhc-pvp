using System.Collections.Generic;
using System.Linq;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;
using UhcPvp.Net;
using UhcPvp.Ui;
using UhcPvp.World;

namespace UhcPvp.Gameplay;

/// <summary>
/// The match: builds the arena, spawns players and keeps everyone's positions in sync.
///
/// Networking model (docs/netcode.md): each player's game owns its own movement and sends its
/// state to the host 60 times a second. The host sends everyone a snapshot of all players 60
/// times a second, and each game draws the other players slightly in the past so they move
/// smoothly.
/// </summary>
public partial class Game : Node3D
{
    private readonly Dictionary<int, Player> _remotePlayers = new();
    /// <summary>Host only: latest state of every player.</summary>
    private readonly Dictionary<int, (double Time, PlayerState State)> _latestStates = new();
    /// <summary>Test stats: where we've seen each other player. Kept after they leave.</summary>
    private readonly Dictionary<int, Aabb> _seenBounds = new();

    public VoxelWorld World { get; private set; } = null!;
    public Player LocalPlayer { get; private set; } = null!;
    public int PlayerCount => _remotePlayers.Count + 1;

    private static Network Net => Network.Instance;

    public override void _Ready()
    {
        World = Arena.Build();
        var renderer = new WorldRenderer { Name = "World" };
        AddChild(renderer);
        renderer.Build(World);
        SetupEnvironment();

        LocalPlayer = SpawnPlayer(Net.MyId, local: true);
        foreach (int id in Session.Instance.Players.Keys.Where(id => id != Net.MyId))
        {
            AddRemote(id);
        }

        AddChild(new Hud { Name = "Hud", Game = this });

        Session.Instance.PlayerAdded += AddRemote;
        Session.Instance.PlayerRemoved += RemoveRemote;
        Net.MessageReceived += OnMessage;
        LaunchArgs.Instance.Quitting += PrintTestReport;
        if (LaunchArgs.Instance.ScreenshotPath.Length > 0)
        {
            GetTree().CreateTimer(LaunchArgs.Instance.ScreenshotAt).Timeout += TakeScreenshot;
        }
        GD.Print($"[game] match started as {LocalPlayer.PlayerName} (id {Net.MyId}, team {LocalPlayer.Team.DisplayName()})");
    }

    public override void _ExitTree()
    {
        Session.Instance.PlayerAdded -= AddRemote;
        Session.Instance.PlayerRemoved -= RemoveRemote;
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
        PlayerState state = LocalPlayer.GetState();
        if (Net.IsHost)
        {
            _latestStates[Net.MyId] = (now, state);
            BroadcastSnapshot();
        }
        else
        {
            Net.SendToHost(new StateUpdate(now, state), reliable: false);
        }
    }

    public override void _Process(double delta)
    {
        double now = Net.HostTime;
        foreach (Player player in _remotePlayers.Values)
        {
            player.UpdateRemote(now);
            TrackSeen(player);
        }
    }

    private Player SpawnPlayer(int id, bool local)
    {
        var player = new Player();
        player.Setup(id, local);
        AddChild(player);
        return player;
    }

    private void AddRemote(int id)
    {
        if (id != Net.MyId && !_remotePlayers.ContainsKey(id))
        {
            _remotePlayers[id] = SpawnPlayer(id, local: false);
        }
    }

    private void RemoveRemote(int id)
    {
        if (_remotePlayers.Remove(id, out Player? player))
        {
            player.QueueFree();
        }
        _latestStates.Remove(id);
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
        }
    }

    private void HostReceiveState(int from, StateUpdate update)
    {
        if (!_remotePlayers.TryGetValue(from, out Player? player) || !update.State.IsValid
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
            if (_remotePlayers.TryGetValue(entry.Id, out Player? player) && entry.State.IsValid
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
            + $"seen={_seenBounds.Count} moving={moving} ping_ms={(int)System.Math.Round(Net.PingSeconds * 1000)}");
    }

    private async void TakeScreenshot()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = LaunchArgs.Instance.ScreenshotPath;
        Error err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[game] screenshot saved to {path} ({err})");
    }
}
