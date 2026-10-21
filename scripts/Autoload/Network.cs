using System;
using System.Linq;
using Godot;
using UhcPvp.Core;
using UhcPvp.Net;

namespace UhcPvp.Autoload;

/// <summary>
/// Networking: hosting, joining, sending messages, clock sync and the lag simulator.
/// All messages travel as bytes through two RPCs (one reliable, one unreliable), which keeps
/// the lag simulator in a single place. Everyone connects to one host; see docs/netcode.md.
/// </summary>
public partial class Network : Node
{
    public const ushort ProtocolVersion = 2;
    public const int HostId = 1;

    private readonly record struct Outgoing(int To, byte[] Data, bool Reliable);
    private readonly record struct Incoming(int From, byte[] Data);

    private readonly LagSimulator<Outgoing> _outgoingLag = new();
    private readonly LagSimulator<Incoming> _incomingLag = new();
    private readonly ulong _startUsec = Time.GetTicksUsec();
    private double _pingTimer;

    public static Network Instance { get; private set; } = null!;

    public event Action? JoinedHost;
    public event Action<string>? JoinFailed;
    public event Action? ConnectionLost;
    public event Action<int>? PeerConnected;
    public event Action<int>? PeerDisconnected;
    public event Action<int, IMessage>? MessageReceived;

    public NetClock Clock { get; } = new();

    /// <summary>Local time in seconds since the game started.</summary>
    public double Now => (Time.GetTicksUsec() - _startUsec) / 1_000_000.0;

    /// <summary>Time on the host's clock: the shared timeline every player stamps events with.</summary>
    public double HostTime => IsHost ? Now : Clock.HostTime(Now);

    public bool IsOnline =>
        Multiplayer.MultiplayerPeer is { } peer and not OfflineMultiplayerPeer
        && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

    public bool IsHost => IsOnline && Multiplayer.IsServer();

    public int MyId => Multiplayer.GetUniqueId();

    /// <summary>Round-trip time to the host in seconds, including simulated lag.</summary>
    public double PingSeconds => IsHost ? 0.0 : Clock.Ping;

    public bool IsLagSimulated => _outgoingLag.IsActive;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += id => PeerConnected?.Invoke((int)id);
        Multiplayer.PeerDisconnected += id => PeerDisconnected?.Invoke((int)id);
        Multiplayer.ConnectedToServer += () => JoinedHost?.Invoke();
        Multiplayer.ConnectionFailed += () =>
        {
            Reset();
            JoinFailed?.Invoke("Couldn't reach the host");
        };
        Multiplayer.ServerDisconnected += () =>
        {
            Reset();
            ConnectionLost?.Invoke();
        };
        var args = LaunchArgs.Instance;
        SetSimulatedLag(args.PingMs, args.JitterMs, args.LossPercent);
    }

    /// <summary>
    /// Adds fake ping and jitter (ms) and packet loss (percent) to this player's connection.
    /// Half the delay is applied on the way out and half on the way back.
    /// </summary>
    public void SetSimulatedLag(double pingMs, double jitterMs, double lossPercent)
    {
        double latency = Math.Max(pingMs, 0.0) / 2000.0;
        double jitter = Math.Max(jitterMs, 0.0) / 2000.0;
        double loss = Math.Clamp(lossPercent / 200.0, 0.0, 1.0);
        _outgoingLag.Latency = _incomingLag.Latency = latency;
        _outgoingLag.Jitter = _incomingLag.Jitter = jitter;
        _outgoingLag.Loss = _incomingLag.Loss = loss;
    }

    public Error HostGame(int port)
    {
        Leave();
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateServer(port, Tuning.MaxPlayers - 1);
        if (err == Error.Ok)
        {
            Multiplayer.MultiplayerPeer = peer;
        }
        return err;
    }

    public Error JoinGame(string address, int port)
    {
        Leave();
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateClient(address, port);
        if (err == Error.Ok)
        {
            Multiplayer.MultiplayerPeer = peer;
        }
        return err;
    }

    public void Leave()
    {
        if (Multiplayer.MultiplayerPeer is { } peer and not OfflineMultiplayerPeer)
        {
            peer.Close();
        }
        Reset();
    }

    /// <summary>Disconnects a player shortly after sending them a reason.</summary>
    public void Kick(int id, string reason)
    {
        Send(id, new Rejected(reason), reliable: true);
        GetTree().CreateTimer(0.3).Timeout += () =>
        {
            if (IsHost && Multiplayer.GetPeers().Contains(id))
            {
                Multiplayer.MultiplayerPeer.DisconnectPeer(id);
            }
        };
    }

    public void Send(int to, IMessage message, bool reliable)
    {
        if (!IsOnline)
        {
            return;
        }
        byte[] data = MessageCodec.Encode(message);
        if (_outgoingLag.IsActive)
        {
            _outgoingLag.Push(new Outgoing(to, data, reliable), Now, reliable);
        }
        else
        {
            Transmit(to, data, reliable);
        }
    }

    public void SendToHost(IMessage message, bool reliable) => Send(HostId, message, reliable);

    /// <summary>Host only: sends a message to every connected player except <paramref name="exceptId"/>.</summary>
    public void Broadcast(IMessage message, bool reliable, int exceptId = 0)
    {
        foreach (int id in Multiplayer.GetPeers())
        {
            if (id != exceptId)
            {
                Send(id, message, reliable);
            }
        }
    }

    public override void _Process(double delta)
    {
        double now = Now;
        foreach (Outgoing packet in _outgoingLag.PopReady(now))
        {
            Transmit(packet.To, packet.Data, packet.Reliable);
        }
        foreach (Incoming packet in _incomingLag.PopReady(now))
        {
            Dispatch(packet.From, packet.Data);
        }
        if (IsOnline && !IsHost)
        {
            _pingTimer -= delta;
            if (_pingTimer <= 0.0)
            {
                _pingTimer = Tuning.PingInterval;
                SendToHost(new Ping(now), reliable: false);
            }
        }
    }

    private void Transmit(int to, byte[] data, bool reliable)
    {
        if (!IsOnline)
        {
            return;
        }
        // Skip players who are mid-disconnect; ENet errors if we send to them.
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet
            && enet.GetPeer(to)?.GetState() != ENetPacketPeer.PeerState.Connected)
        {
            return;
        }
        RpcId(to, reliable ? MethodName.ReceiveReliable : MethodName.ReceiveUnreliable, data);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void ReceiveReliable(byte[] data) => OnPacket(Multiplayer.GetRemoteSenderId(), data, reliable: true);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void ReceiveUnreliable(byte[] data) => OnPacket(Multiplayer.GetRemoteSenderId(), data, reliable: false);

    private void OnPacket(int from, byte[] data, bool reliable)
    {
        if (_incomingLag.IsActive)
        {
            _incomingLag.Push(new Incoming(from, data), Now, reliable);
        }
        else
        {
            Dispatch(from, data);
        }
    }

    private void Dispatch(int from, byte[] data)
    {
        switch (MessageCodec.Decode(data))
        {
            case null:
                GD.PushWarning($"[net] dropped a malformed message from {from}");
                break;
            case Ping ping when IsHost:
                Send(from, new Pong(ping.ClientTime, Now), reliable: false);
                break;
            case Pong pong when !IsHost:
                Clock.AddSample(pong.ClientTime, pong.HostTime, Now);
                break;
            case Ping or Pong:
                break;
            case var message:
                MessageReceived?.Invoke(from, message);
                break;
        }
    }

    private void Reset()
    {
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        _outgoingLag.Clear();
        _incomingLag.Clear();
        Clock.Reset();
        _pingTimer = 0.0;
    }
}
