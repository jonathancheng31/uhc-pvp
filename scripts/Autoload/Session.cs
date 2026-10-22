using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UhcPvp.Core;
using UhcPvp.Net;

namespace UhcPvp.Autoload;

/// <summary>
/// Who is in the match (names and teams), and moving between the menu and the match. Lives
/// across scene changes, so the roster survives loading the arena.
/// </summary>
public partial class Session : Node
{
    public const string MenuScene = "res://scenes/main_menu.tscn";
    public const string GameScene = "res://scenes/game.tscn";
    public const int MaxNameLength = 16;

    private readonly Dictionary<int, RosterEntry> _players = new();
    private (string Address, int Port, int RetriesLeft)? _joinAttempt;
    private int _nextBotId = -1;

    public static Session Instance { get; private set; } = null!;

    public event Action<int>? PlayerAdded;
    public event Action<int>? PlayerRemoved;
    /// <summary>The match rules changed (the host moved a twist's slider).</summary>
    public event Action? RulesChanged;

    public string MyName { get; private set; } = "Player";
    /// <summary>Shown on the main menu, e.g. why the last match ended.</summary>
    public string Status { get; private set; } = "";
    public bool InMatch { get; private set; }
    /// <summary>Hosting a solo practice match (starts with a training dummy).</summary>
    public bool IsPractice { get; private set; }

    public IReadOnlyDictionary<int, RosterEntry> Players => _players;

    /// <summary>The match's rules (our twists). The host's choice, the same on every player's game.</summary>
    public MatchRules Rules { get; private set; } = MatchRules.Default;

    private static Network Net => Network.Instance;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Net.JoinedHost += OnJoinedHost;
        Net.JoinFailed += OnJoinFailed;
        Net.ConnectionLost += () => Leave("Lost connection to the host");
        Net.PeerDisconnected += OnPeerDisconnected;
        Net.MessageReceived += OnMessage;
    }

    public Error Host(string playerName, int port, bool practice = false)
    {
        MyName = CleanName(playerName);
        IsPractice = practice;
        Error err = Net.HostGame(port);
        if (err != Error.Ok)
        {
            Status = $"Couldn't host on port {port} (is it already in use?)";
            return err;
        }
        _players.Clear();
        _players[Network.HostId] = new RosterEntry(Network.HostId, MyName, Team.Red, false);
        _nextBotId = -1;
        // The host's last choice, kept between matches (or a test's --strafe).
        Rules = LaunchArgs.Instance.StrafeSpeed is double strafe
            ? new MatchRules(strafe).Normalized()
            : Settings.Instance.HostRules;
        GD.Print($"[session] hosting on port {port} as {MyName}");
        EnterMatch();
        return Error.Ok;
    }

    public Error Join(string playerName, string address, int port, int retries = 0)
    {
        MyName = CleanName(playerName);
        IsPractice = false;
        _joinAttempt = (address, port, retries);
        Status = $"Connecting to {address}:{port}...";
        return Net.JoinGame(address, port);
    }

    public void Leave(string reason = "")
    {
        Net.Leave();
        _players.Clear();
        _joinAttempt = null;
        Rules = MatchRules.Default;
        Status = reason;
        if (InMatch)
        {
            InMatch = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetTree().ChangeSceneToFile(MenuScene);
        }
    }

    /// <summary>Host only: adds a practice bot to a team. Returns its id (negative), or 0 if not hosting or full.</summary>
    public int AddBot(Team team)
    {
        if (!Net.IsHost || _players.Count >= Tuning.MaxPlayers)
        {
            return 0;
        }
        int id = _nextBotId--;
        var entry = new RosterEntry(id, $"Bot {-id}", team, true);
        _players[id] = entry;
        Net.Broadcast(new PlayerJoined(entry), reliable: true);
        PlayerAdded?.Invoke(id);
        return id;
    }

    /// <summary>Host only: changes the match rules for everyone, and remembers them for the next match.</summary>
    public void SetRules(MatchRules rules)
    {
        rules = rules.Normalized();
        if (!Net.IsHost || rules == Rules)
        {
            return;
        }
        Rules = rules;
        Settings.Instance.ChangeHostRules(rules);
        Net.Broadcast(RulesUpdate.From(rules), reliable: true);
        RulesChanged?.Invoke();
    }

    /// <summary>Host only: removes a practice bot.</summary>
    public void RemoveBot(int id)
    {
        if (Net.IsHost && IsBot(id) && _players.Remove(id))
        {
            Net.Broadcast(new PlayerLeft(id), reliable: true);
            PlayerRemoved?.Invoke(id);
        }
    }

    public bool IsBot(int id) => _players.TryGetValue(id, out var entry) && entry.IsBot;

    /// <summary>The peer whose game runs this player: themselves, or the host for bots.</summary>
    public static int OwnerOf(int id) => id < 0 ? Network.HostId : id;

    public Team TeamOf(int id) => _players.TryGetValue(id, out var entry) ? entry.Team : Team.Red;

    public string NameOf(int id) => _players.TryGetValue(id, out var entry) ? entry.Name : "?";

    /// <summary>A player's position within their team, used to pick a spawn spot.</summary>
    public int SlotOf(int id)
    {
        Team team = TeamOf(id);
        var ids = _players.Keys.Where(other => TeamOf(other) == team).Order().ToList();
        return Math.Max(ids.IndexOf(id), 0);
    }

    public int TeamSize(Team team) => _players.Values.Count(p => p.Team == team);

    public static string CleanName(string raw)
    {
        string cleaned = raw.Trim();
        if (cleaned.Length > MaxNameLength)
        {
            cleaned = cleaned[..MaxNameLength];
        }
        return cleaned.Length > 0 ? cleaned : "Player";
    }

    private void EnterMatch()
    {
        InMatch = true;
        Status = "";
        GetTree().ChangeSceneToFile(GameScene);
    }

    private void OnJoinedHost() =>
        Net.SendToHost(new Hello(Network.ProtocolVersion, MyName), reliable: true);

    private void OnJoinFailed(string reason)
    {
        if (_joinAttempt is { RetriesLeft: > 0 } attempt)
        {
            _joinAttempt = attempt with { RetriesLeft = attempt.RetriesLeft - 1 };
            GetTree().CreateTimer(0.5).Timeout += () =>
            {
                if (_joinAttempt is { } retry)
                {
                    Net.JoinGame(retry.Address, retry.Port);
                }
            };
            return;
        }
        _joinAttempt = null;
        Leave(reason);
    }

    private void OnPeerDisconnected(int id)
    {
        if (!Net.IsHost || !_players.Remove(id, out var entry))
        {
            return;
        }
        GD.Print($"[session] {entry.Name} left");
        Net.Broadcast(new PlayerLeft(id), reliable: true);
        PlayerRemoved?.Invoke(id);
    }

    private void OnMessage(int from, IMessage message)
    {
        if (Net.IsHost)
        {
            if (message is Hello hello)
            {
                HostHandleHello(from, hello);
            }
            return;
        }
        switch (message)
        {
            case Welcome welcome:
                ClientHandleWelcome(welcome);
                break;
            case Rejected rejected:
                Leave(rejected.Reason);
                break;
            case PlayerJoined joined when !_players.ContainsKey(joined.Player.Id):
                _players[joined.Player.Id] = joined.Player;
                PlayerAdded?.Invoke(joined.Player.Id);
                break;
            case PlayerLeft left when _players.Remove(left.Id):
                PlayerRemoved?.Invoke(left.Id);
                break;
            case RulesUpdate update when from == Network.HostId:
                Rules = update.ToMatchRules();
                RulesChanged?.Invoke();
                break;
        }
    }

    private void HostHandleHello(int from, Hello hello)
    {
        if (_players.ContainsKey(from))
        {
            return;
        }
        if (hello.ProtocolVersion != Network.ProtocolVersion)
        {
            Net.Kick(from, "Version mismatch: update your game");
            return;
        }
        if (_players.Count >= Tuning.MaxPlayers)
        {
            Net.Kick(from, "Match is full");
            return;
        }
        Team team = TeamSize(Team.Red) <= TeamSize(Team.Blue) ? Team.Red : Team.Blue;
        var entry = new RosterEntry(from, CleanName(hello.Name), team, false);
        _players[from] = entry;
        GD.Print($"[session] {entry.Name} joined team {team.DisplayName()}");
        Net.Send(from, new Welcome(from, _players.Values.ToArray()), reliable: true);
        Net.Send(from, RulesUpdate.From(Rules), reliable: true);
        Net.Broadcast(new PlayerJoined(entry), reliable: true, exceptId: from);
        PlayerAdded?.Invoke(from);
    }

    private void ClientHandleWelcome(Welcome welcome)
    {
        _players.Clear();
        foreach (RosterEntry entry in welcome.Roster)
        {
            _players[entry.Id] = entry;
        }
        _joinAttempt = null;
        GD.Print($"[session] joined as {MyName} on team {TeamOf(Net.MyId).DisplayName()}");
        EnterMatch();
    }
}
