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
}

/// <summary>A network message. Each one knows how to write itself; <see cref="MessageCodec"/> reads them.</summary>
public interface IMessage
{
    MsgType Type { get; }
    void Write(BinaryWriter w);
}

public readonly record struct RosterEntry(int Id, string Name, Team Team);

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
    }

    internal static RosterEntry ReadRosterEntry(this BinaryReader r)
    {
        int id = r.ReadInt32();
        string name = r.ReadShortString();
        byte team = r.ReadByte();
        return team <= (byte)Team.Blue
            ? new RosterEntry(id, name, (Team)team)
            : throw new InvalidDataException("bad team");
    }

    internal static void WritePlayerState(this BinaryWriter w, PlayerState s)
    {
        w.Write(s.Position.X);
        w.Write(s.Position.Y);
        w.Write(s.Position.Z);
        w.Write(s.Yaw);
        w.Write(s.Pitch);
        w.Write((byte)s.Flags);
    }

    internal static PlayerState ReadPlayerState(this BinaryReader r) => new(
        new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
        r.ReadSingle(),
        r.ReadSingle(),
        (PlayerFlags)r.ReadByte());
}
