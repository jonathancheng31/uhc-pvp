using System;
using System.Collections.Generic;

namespace UhcPvp.Core;

/// <summary>
/// Estimates the host's clock from ping/pong round trips, so every player can stamp events
/// on the same timeline. That shared timeline is what the lag cap and the smoothing of other
/// players rely on.
/// </summary>
public sealed class NetClock
{
    public const int Window = 20;

    private readonly Queue<(double Rtt, double Offset)> _samples = new();

    /// <summary>host_time - local_time, in seconds.</summary>
    public double Offset { get; private set; }
    /// <summary>Smoothed round-trip time in seconds, for display.</summary>
    public double Ping { get; private set; }
    public bool HasSample { get; private set; }

    public void AddSample(double sentLocal, double hostTimeAtReply, double receivedLocal)
    {
        double rtt = Math.Max(receivedLocal - sentLocal, 0.0);
        _samples.Enqueue((rtt, hostTimeAtReply + rtt * 0.5 - receivedLocal));
        if (_samples.Count > Window)
        {
            _samples.Dequeue();
        }
        // The fastest recent round trip had the least queuing delay, so its offset is the
        // most trustworthy.
        var best = (Rtt: double.MaxValue, Offset: 0.0);
        foreach (var sample in _samples)
        {
            if (sample.Rtt < best.Rtt)
            {
                best = sample;
            }
        }
        Offset = best.Offset;
        Ping = HasSample ? Ping + (rtt - Ping) * 0.2 : rtt;
        HasSample = true;
    }

    public double HostTime(double localNow) => localNow + Offset;

    public void Reset()
    {
        Offset = 0.0;
        Ping = 0.0;
        HasSample = false;
        _samples.Clear();
    }
}
