using System;
using System.Linq;
using Godot;
using UhcPvp.Core;
using UhcPvp.Net;

namespace UhcPvp.Tests;

public class MessageTests : TestCase
{
    private static readonly PlayerState SampleState =
        new(new Vector3(1.5f, 5f, -3.25f), 90f, -12.5f, PlayerFlags.OnGround | PlayerFlags.Sprinting, 17.5f);

    public void TestEveryMessageRoundTrips()
    {
        IMessage[] messages =
        [
            new Hello(Autoload.Network.ProtocolVersion, "Steve"),
            new Welcome(7, [new RosterEntry(1, "Host", Team.Red, false), new RosterEntry(-1, "Dummy 1", Team.Blue, true)]),
            new Rejected("Match is full"),
            new PlayerJoined(new RosterEntry(9, "Sam", Team.Blue, false)),
            new PlayerLeft(9),
            new StateUpdate(12.25, SampleState),
            new Snapshot([new SnapshotEntry(1, 3.5, SampleState), new SnapshotEntry(7, 3.25, SampleState)]),
            new Ping(1.5),
            new Pong(1.5, 99.75),
            new Attack(7, -1, 10.5, 10.4, new Vector3(1, 5, 2), 45f, new Vector3(1, 5, -0.5f), AttackFlags.Critical | AttackFlags.SprintKnockback),
            new Hurt(-1, 7, 14.5f, HurtFlags.Fresh | HurtFlags.Critical),
            new Swing(7),
        ];
        Check(messages.Select(m => m.Type).Distinct().Count() == Enum.GetValues<MsgType>().Length,
            "test covers every message type");
        foreach (IMessage message in messages)
        {
            IMessage? decoded = MessageCodec.Decode(MessageCodec.Encode(message));
            Check(decoded is not null && decoded.GetType() == message.GetType(), $"{message.Type} decodes");
            CheckEqual(MessageCodec.Encode(decoded!).Length, MessageCodec.Encode(message).Length, $"{message.Type} size");
            Check(MessageCodec.Encode(decoded!).SequenceEqual(MessageCodec.Encode(message)), $"{message.Type} same bytes");
        }
    }

    public void TestTruncatedAndPaddedMessagesAreRejected()
    {
        byte[] data = MessageCodec.Encode(new StateUpdate(1.0, SampleState));
        for (int length = 0; length < data.Length; length++)
        {
            Check(MessageCodec.Decode(data[..length]) is null, $"truncated to {length} bytes rejected");
        }
        Check(MessageCodec.Decode([.. data, 0]) is null, "trailing byte rejected");
    }

    public void TestGarbageIsRejected()
    {
        var random = new Random(5);
        for (int i = 0; i < 2000; i++)
        {
            byte[] junk = new byte[random.Next(0, 40)];
            random.NextBytes(junk);
            // Must never throw; may occasionally decode to something valid.
            _ = MessageCodec.Decode(junk);
        }
        Check(MessageCodec.Decode([255]) is null, "unknown type rejected");
    }

    public void TestLongNamesAreCappedAndCountsLimited()
    {
        var hello = (Hello)MessageCodec.Decode(MessageCodec.Encode(new Hello(1, new string('x', 500))))!;
        Check(hello.Name.Length <= MessageCodec.MaxStringBytes, "name capped");
        var tooMany = new Snapshot(Enumerable.Range(0, Tuning.MaxPlayers + 1)
            .Select(i => new SnapshotEntry(i, 0, SampleState)).ToArray());
        Check(MessageCodec.Decode(MessageCodec.Encode(tooMany)) is null, "oversized snapshot rejected");
    }
}
