using System.Collections.Generic;
using System.Linq;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;

namespace UhcPvp.Tests;

public class LagSimulatorTests : TestCase
{
    public void TestDelaysPacketsByLatency()
    {
        var sim = new LagSimulator<string>(42) { Latency = 0.05 };
        sim.Push("a", 1.0, reliable: false);
        CheckEqual(sim.PopReady(1.049).Count, 0, "not due yet");
        CheckEqual(string.Join(",", sim.PopReady(1.05)), "a", "due at send time + latency");
        CheckEqual(sim.Pending, 0, "queue emptied");
    }

    public void TestReliablePacketsKeepTheirOrderUnderJitter()
    {
        var sim = new LagSimulator<int>(7) { Latency = 0.05, Jitter = 0.2 };
        for (int i = 0; i < 50; i++)
        {
            sim.Push(i, 1.0 + i * 0.001, reliable: true);
        }
        CheckEqual(string.Join(",", sim.PopReady(10.0)), string.Join(",", Enumerable.Range(0, 50)), "reliable order");
    }

    public void TestReliablePacketsAreNeverLost()
    {
        var sim = new LagSimulator<int>(3) { Loss = 1.0 };
        for (int i = 0; i < 20; i++)
        {
            Check(sim.Push(i, 0.0, reliable: true), "reliable push accepted");
        }
        CheckEqual(sim.PopReady(1.0).Count, 20, "all reliable packets delivered");
    }

    public void TestUnreliableLossRateIsRoughlyRight()
    {
        var sim = new LagSimulator<int>(99) { Loss = 0.25 };
        int lost = Enumerable.Range(0, 4000).Count(i => !sim.Push(i, 0.0, reliable: false));
        CheckNear(lost / 4000.0, 0.25, 0.03, "loss rate");
    }

    public void TestInactiveByDefault() => Check(!new LagSimulator<int>().IsActive, "no lag unless configured");
}

public class NetClockTests : TestCase
{
    public void TestEstimatesOffsetFromSymmetricRoundTrip()
    {
        var clock = new NetClock();
        // Host clock is 100 s ahead; 40 ms each way.
        clock.AddSample(5.0, 105.04, 5.08);
        CheckNear(clock.Offset, 100.0, 1e-9, "offset");
        CheckNear(clock.Ping, 0.08, 1e-9, "ping");
        CheckNear(clock.HostTime(6.0), 106.0, 1e-9, "host time");
    }

    public void TestPrefersFastestRoundTrip()
    {
        var clock = new NetClock();
        clock.AddSample(1.0, 101.3, 1.4); // slow, skewed
        clock.AddSample(2.0, 102.02, 2.04); // fast, accurate
        clock.AddSample(3.0, 103.25, 3.3); // slow again
        CheckNear(clock.Offset, 100.0, 1e-9, "offset comes from the fastest sample");
    }
}

public class SnapshotBufferTests : TestCase
{
    private static PlayerState State(float x, float yaw) => new(new Vector3(x, 5, 0), yaw, 0, PlayerFlags.None, 20);

    public void TestBlendsBetweenStates()
    {
        var buffer = new SnapshotBuffer();
        buffer.Push(1.0, State(0, 0));
        buffer.Push(2.0, State(10, 90));
        PlayerState mid = buffer.Sample(1.5);
        CheckNear(mid.Position.X, 5.0, 1e-5, "x halfway");
        CheckNear(mid.Yaw, 45.0, 1e-4, "yaw halfway");
    }

    public void TestYawBlendsTheShortWayRound()
    {
        var buffer = new SnapshotBuffer();
        buffer.Push(1.0, State(0, 170));
        buffer.Push(2.0, State(0, -170));
        CheckNear(buffer.Sample(1.5).Yaw, 180.0, 1e-3, "crosses 180, not 0");
    }

    public void TestHoldsEndsOutsideRange()
    {
        var buffer = new SnapshotBuffer();
        buffer.Push(1.0, State(1, 0));
        buffer.Push(2.0, State(2, 0));
        CheckNear(buffer.Sample(0.0).Position.X, 1.0, 1e-6, "before first");
        CheckNear(buffer.Sample(9.0).Position.X, 2.0, 1e-6, "after last");
    }

    public void TestIgnoresOldAndRepeatedStates()
    {
        var buffer = new SnapshotBuffer();
        Check(buffer.Push(2.0, State(2, 0)), "first state stored");
        Check(!buffer.Push(2.0, State(9, 0)), "repeat ignored");
        Check(!buffer.Push(1.0, State(9, 0)), "older ignored");
        CheckEqual(buffer.Count, 1, "one state kept");
    }

    public void TestRenderDelayFollowsArrivalAge()
    {
        var buffer = new SnapshotBuffer();
        for (int i = 0; i < 400; i++)
        {
            buffer.NoteArrivalAge(0.06);
        }
        // Steady 60 ms arrivals: delay settles at 60 ms plus one send interval.
        CheckNear(buffer.RenderDelay, 0.06 + 1.0 / Tuning.StateSendRate, 0.002, "settled delay");
    }

    public void TestRenderDelayNeverBelowMinimum()
    {
        var buffer = new SnapshotBuffer();
        buffer.NoteArrivalAge(-1.0);
        Check(buffer.RenderDelay >= Tuning.InterpMinDelay, "minimum delay");
    }
}

public class TuningTests : TestCase
{
    public void TestDefaultSensitivityMatchesMinecraft() =>
        // Minecraft at "100%" turns 0.15 degrees per mouse count.
        CheckNear(Tuning.DegreesPerMouseCount(0.5), 0.15, 1e-12, "100% sensitivity");

    public void TestPlayerStateValidity()
    {
        var state = new PlayerState(new Vector3(1, 2, 3), 45, -10, PlayerFlags.Sprinting, 20);
        Check(state.IsValid, "valid");
        Check(state.Has(PlayerFlags.Sprinting) && !state.Has(PlayerFlags.Sneaking), "flags");
        Check(!(state with { Yaw = float.NaN }).IsValid, "NaN rejected");
    }
}

public class LaunchArgsTests : TestCase
{
    public void TestScreenshotTimesParseAList()
    {
        double[] times = LaunchArgs.ParseTimes(" 1, 2.5,3 ");
        Check(times.Length == 3 && times[0] == 1 && times[1] == 2.5 && times[2] == 3, $"got [{string.Join(", ", times)}]");
    }

    public void TestBadScreenshotTimesFallBackToDefault()
    {
        Check(LaunchArgs.ParseTimes("soon,-1").Length == 1, "falls back to one default time");
        Check(LaunchArgs.ParseTimes("x,4").Length == 1, "skips the bad entry");
    }

    public void TestNumberedScreenshotPath() =>
        Check(LaunchArgs.NumberedPath("/tmp/shots/shot.png", 2) == "/tmp/shots/shot_2.png", "inserts the number before .png");
}
