using System;
using System.IO;
using System.Text;
using Godot;
using UhcPvp.Core;

namespace UhcPvp.Net;

/// <summary>Message ids on the wire. Never reorder; add new ones at the end.</summary>
public enum MsgType : byte
{
    Hello,
    Welcome,
    Rejected,
    PlayerJoined,
    PlayerLeft,
    StateUpdate,
    Snapshot,
    Ping,
    Pong,
    Attack,
    Hurt,
    Swing,
    RodCast,
    RodHook,
    RulesUpdate,
}

/// <summary>A network message. Each one knows how to write itself; <see cref="MessageCodec"/> reads them.</summary>
public interface IMessage
{
    MsgType Type { get; }
    void Write(BinaryWriter w);
}

/// <summary>A player in the match. Bots have negative ids and are run by the host.</summary>
public readonly record struct RosterEntry(int Id, string Name, Team Team, bool IsBot);

public readonly record struct SnapshotEntry(int Id, double Time, PlayerState State);

/// <summary>client -> host: asks to join.</summary>
public sealed record Hello(ushort ProtocolVersion, string Name) : IMessage
{
    public MsgType Type => MsgType.Hello;
    public void Write(BinaryWriter w)
    {
        w.Write(ProtocolVersion);
        w.WriteShortString(Name);
    }
    public static Hello Read(BinaryReader r) => new(r.ReadUInt16(), r.ReadShortString());
}

/// <summary>host -> client: you're in; here's your id and everyone in the match.</summary>
public sealed record Welcome(int YourId, RosterEntry[] Roster) : IMessage
{
    public MsgType Type => MsgType.Welcome;
    public void Write(BinaryWriter w)
    {
        w.Write(YourId);
        w.Write((byte)Roster.Length);
        foreach (var entry in Roster)
        {
            w.WriteRosterEntry(entry);
        }
    }
    public static Welcome Read(BinaryReader r)
    {
        int id = r.ReadInt32();
        var roster = new RosterEntry[r.ReadCount(Tuning.MaxPlayers)];
        for (int i = 0; i < roster.Length; i++)
        {
            roster[i] = r.ReadRosterEntry();
        }
        return new Welcome(id, roster);
    }
}

/// <summary>host -> client: you can't join, and why.</summary>
public sealed record Rejected(string Reason) : IMessage
{
    public MsgType Type => MsgType.Rejected;
    public void Write(BinaryWriter w) => w.WriteShortString(Reason);
    public static Rejected Read(BinaryReader r) => new(r.ReadShortString());
}

/// <summary>host -> all: someone joined.</summary>
public sealed record PlayerJoined(RosterEntry Player) : IMessage
{
    public MsgType Type => MsgType.PlayerJoined;
    public void Write(BinaryWriter w) => w.WriteRosterEntry(Player);
    public static PlayerJoined Read(BinaryReader r) => new(r.ReadRosterEntry());
}

/// <summary>host -> all: someone left.</summary>
public sealed record PlayerLeft(int Id) : IMessage
{
    public MsgType Type => MsgType.PlayerLeft;
    public void Write(BinaryWriter w) => w.Write(Id);
    public static PlayerLeft Read(BinaryReader r) => new(r.ReadInt32());
}

/// <summary>client -> host: my state at this host-clock time.</summary>
public sealed record StateUpdate(double Time, PlayerState State) : IMessage
{
    public MsgType Type => MsgType.StateUpdate;
    public void Write(BinaryWriter w)
    {
        w.Write(Time);
        w.WritePlayerState(State);
    }
    public static StateUpdate Read(BinaryReader r) => new(r.ReadDouble(), r.ReadPlayerState());
}

/// <summary>host -> all: the latest state of every player.</summary>
public sealed record Snapshot(SnapshotEntry[] Players) : IMessage
{
    public MsgType Type => MsgType.Snapshot;
    public void Write(BinaryWriter w)
    {
        w.Write((byte)Players.Length);
        foreach (var p in Players)
        {
            w.Write(p.Id);
            w.Write(p.Time);
            w.WritePlayerState(p.State);
        }
    }
    public static Snapshot Read(BinaryReader r)
    {
        var players = new SnapshotEntry[r.ReadCount(Tuning.MaxPlayers)];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = new SnapshotEntry(r.ReadInt32(), r.ReadDouble(), r.ReadPlayerState());
        }
        return new Snapshot(players);
    }
}

/// <summary>client -> host: clock-sync ping.</summary>
public sealed record Ping(double ClientTime) : IMessage
{
    public MsgType Type => MsgType.Ping;
    public void Write(BinaryWriter w) => w.Write(ClientTime);
    public static Ping Read(BinaryReader r) => new(r.ReadDouble());
}

/// <summary>host -> client: clock-sync reply with the host's time.</summary>
public sealed record Pong(double ClientTime, double HostTime) : IMessage
{
    public MsgType Type => MsgType.Pong;
    public void Write(BinaryWriter w)
    {
        w.Write(ClientTime);
        w.Write(HostTime);
    }
    public static Pong Read(BinaryReader r) => new(r.ReadDouble(), r.ReadDouble());
}

[Flags]
public enum AttackFlags : byte
{
    None = 0,
    /// <summary>The attacker was falling: a critical hit.</summary>
    Critical = 1,
    /// <summary>The attacker's sprint counts for sprint knockback (see PlayerMotor.ServerSprinting).</summary>
    SprintKnockback = 2,
    /// <summary>
    /// A fishing rod hit: no damage, knockback away from the thrower, counts for hit protection.
    /// <c>AttackerFeet</c> is the thrower's position when the bobber touched.
    /// </summary>
    Rod = 4,
}

/// <summary>
/// attacker -> host -> victim's owner: "I hit you". The attacker's game decided the hit from what
/// it saw; the victim's game checks the lag cap and applies damage and knockback.
/// </summary>
/// <param name="AttackTime">Host-clock time of the click.</param>
/// <param name="ViewTime">Host-clock time of the victim state the attacker was looking at.</param>
/// <param name="TargetFeet">Where the attacker saw the victim.</param>
public sealed record Attack(
    int AttackerId, int TargetId, double AttackTime, double ViewTime,
    Vector3 AttackerFeet, float AttackerYaw, Vector3 TargetFeet, AttackFlags Flags) : IMessage
{
    public MsgType Type => MsgType.Attack;
    public void Write(BinaryWriter w)
    {
        w.Write(AttackerId);
        w.Write(TargetId);
        w.Write(AttackTime);
        w.Write(ViewTime);
        w.WriteVector3(AttackerFeet);
        w.Write(AttackerYaw);
        w.WriteVector3(TargetFeet);
        w.Write((byte)Flags);
    }
    public static Attack Read(BinaryReader r) => new(
        r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(),
        r.ReadVector3(), r.ReadSingle(), r.ReadVector3(), (AttackFlags)r.ReadByte());
}

[Flags]
public enum HurtFlags : byte
{
    None = 0,
    /// <summary>A full hit: knockback, red flash and hurt sound.</summary>
    Fresh = 1,
    Critical = 2,
    Killed = 4,
    /// <summary>A fishing rod hit.</summary>
    Rod = 8,
}

/// <summary>
/// victim's owner -> host -> all: a hit landed (a full hit, or without <see cref="HurtFlags.Fresh"/>
/// a stronger hit during hit protection), and the victim's health now. <c>AttackTime</c> is the
/// host time of the click, which hit protection is counted from.
/// </summary>
public sealed record Hurt(int VictimId, int AttackerId, float Health, HurtFlags Flags, double AttackTime) : IMessage
{
    public MsgType Type => MsgType.Hurt;
    public void Write(BinaryWriter w)
    {
        w.Write(VictimId);
        w.Write(AttackerId);
        w.Write(Health);
        w.Write((byte)Flags);
        w.Write(AttackTime);
    }
    public static Hurt Read(BinaryReader r) =>
        new(r.ReadInt32(), r.ReadInt32(), r.ReadSingle(), (HurtFlags)r.ReadByte(), r.ReadDouble());
}

/// <summary>player -> host -> all: someone swung their arm (attacked, hit or not).</summary>
public sealed record Swing(int PlayerId) : IMessage
{
    public MsgType Type => MsgType.Swing;
    public void Write(BinaryWriter w) => w.Write(PlayerId);
    public static Swing Read(BinaryReader r) => new(r.ReadInt32());
}

/// <summary>
/// thrower -> host -> all: someone threw their fishing rod at host time <c>Time</c>. Their old
/// bobber vanishes, and every game flies the new one from <c>Origin</c> with <c>Velocity</c>
/// (blocks per Minecraft tick), so they all compute the same path.
/// </summary>
public sealed record RodCast(int PlayerId, double Time, Vector3 Origin, Vector3 Velocity) : IMessage
{
    public MsgType Type => MsgType.RodCast;
    public void Write(BinaryWriter w)
    {
        w.Write(PlayerId);
        w.Write(Time);
        w.WriteVector3(Origin);
        w.WriteVector3(Velocity);
    }
    public static RodCast Read(BinaryReader r) => new(r.ReadInt32(), r.ReadDouble(), r.ReadVector3(), r.ReadVector3());
}

/// <summary>
/// thrower -> host -> all: the thrower's bobber caught <c>VictimId</c> at host time <c>Time</c>
/// and hangs on them now. (The hit itself travels as an <see cref="Attack"/> with
/// <see cref="AttackFlags.Rod"/>.)
/// </summary>
public sealed record RodHook(int PlayerId, int VictimId, double Time) : IMessage
{
    public MsgType Type => MsgType.RodHook;
    public void Write(BinaryWriter w)
    {
        w.Write(PlayerId);
        w.Write(VictimId);
        w.Write(Time);
    }
    public static RodHook Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadDouble());
}

/// <summary>
/// host -> all: the match's rules (our twists, see <see cref="MatchRules"/>), sent to each player
/// as they join and to everyone whenever the host changes them.
/// </summary>
public sealed record RulesUpdate(float StrafeSpeed, float StrafeGrip, float WastedClickDelay, float ComboKnockback) : IMessage
{
    public MsgType Type => MsgType.RulesUpdate;
    public void Write(BinaryWriter w)
    {
        w.Write(StrafeSpeed);
        w.Write(StrafeGrip);
        w.Write(WastedClickDelay);
        w.Write(ComboKnockback);
    }
    public static RulesUpdate Read(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    public static RulesUpdate From(MatchRules rules) =>
        new((float)rules.StrafeSpeed, (float)rules.StrafeGrip, (float)rules.WastedClickDelay, (float)rules.ComboKnockback);

    /// <summary>The rules this carries, made valid (a float can't hold 1.15 exactly, and bad values become the default).</summary>
    public MatchRules ToMatchRules() => new MatchRules(StrafeSpeed, StrafeGrip, WastedClickDelay, ComboKnockback).Normalized();
}

/// <summary>Turns messages into bytes and back. Bad input decodes to null rather than throwing.</summary>
public static class MessageCodec
{
    public const int MaxStringBytes = 64;

    public static byte[] Encode(IMessage message)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)message.Type);
            message.Write(writer);
        }
        return stream.ToArray();
    }

    public static IMessage? Decode(byte[] data)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data, writable: false), Encoding.UTF8);
            IMessage? message = (MsgType)reader.ReadByte() switch
            {
                MsgType.Hello => Hello.Read(reader),
                MsgType.Welcome => Welcome.Read(reader),
                MsgType.Rejected => Rejected.Read(reader),
                MsgType.PlayerJoined => PlayerJoined.Read(reader),
                MsgType.PlayerLeft => PlayerLeft.Read(reader),
                MsgType.StateUpdate => StateUpdate.Read(reader),
                MsgType.Snapshot => Snapshot.Read(reader),
                MsgType.Ping => Ping.Read(reader),
                MsgType.Pong => Pong.Read(reader),
                MsgType.Attack => Attack.Read(reader),
                MsgType.Hurt => Hurt.Read(reader),
                MsgType.Swing => Swing.Read(reader),
                MsgType.RodCast => RodCast.Read(reader),
                MsgType.RodHook => RodHook.Read(reader),
                MsgType.RulesUpdate => RulesUpdate.Read(reader),
                _ => null,
            };
            // Trailing bytes mean the sender and receiver disagree about the format.
            return reader.BaseStream.Position == data.Length ? message : null;
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or IOException or DecoderFallbackException)
        {
            return null;
        }
    }

    internal static void WriteShortString(this BinaryWriter w, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxStringBytes)
        {
            Array.Resize(ref bytes, MaxStringBytes);
        }
        w.Write((byte)bytes.Length);
        w.Write(bytes);
    }

    internal static string ReadShortString(this BinaryReader r)
    {
        int length = r.ReadByte();
        if (length > MaxStringBytes)
        {
            throw new InvalidDataException("string too long");
        }
        byte[] bytes = r.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException();
        }
        return Encoding.UTF8.GetString(bytes);
    }

    internal static int ReadCount(this BinaryReader r, int max)
    {
        int count = r.ReadByte();
        return count <= max ? count : throw new InvalidDataException("too many entries");
    }

    internal static void WriteRosterEntry(this BinaryWriter w, RosterEntry entry)
    {
        w.Write(entry.Id);
        w.WriteShortString(entry.Name);
        w.Write((byte)entry.Team);
        w.Write(entry.IsBot);
    }

    internal static RosterEntry ReadRosterEntry(this BinaryReader r)
    {
        int id = r.ReadInt32();
        string name = r.ReadShortString();
        byte team = r.ReadByte();
        bool isBot = r.ReadBoolean();
        return team <= (byte)Team.Blue
            ? new RosterEntry(id, name, (Team)team, isBot)
            : throw new InvalidDataException("bad team");
    }

    internal static void WriteVector3(this BinaryWriter w, Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    internal static Vector3 ReadVector3(this BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    internal static void WritePlayerState(this BinaryWriter w, PlayerState s)
    {
        w.WriteVector3(s.Position);
        w.Write(s.Yaw);
        w.Write(s.Pitch);
        w.Write((byte)s.Flags);
        w.Write(s.Health);
    }

    internal static PlayerState ReadPlayerState(this BinaryReader r) => new(
        r.ReadVector3(),
        r.ReadSingle(),
        r.ReadSingle(),
        (PlayerFlags)r.ReadByte(),
        r.ReadSingle());
}
