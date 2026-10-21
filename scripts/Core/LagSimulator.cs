using System;
using System.Collections.Generic;

namespace UhcPvp.Core;

/// <summary>
/// Delays and drops packets to imitate a bad connection while testing.
/// Reliable packets are never dropped and always come out in the order they went in, like
/// ENet's reliable channel. Unreliable packets can be dropped and, with jitter, can arrive
/// out of order.
/// </summary>
public sealed class LagSimulator<T>
{
    private readonly Random _random;
    private readonly List<(double DeliverAt, long Order, T Packet)> _queue = new();
    private long _order;
    private double _lastReliableAt;

    public LagSimulator(int? seed = null)
    {
        _random = seed is int s ? new Random(s) : new Random();
    }

    /// <summary>One-way delay in seconds.</summary>
    public double Latency { get; set; }
    /// <summary>Random extra delay added to each packet, from 0 up to this many seconds.</summary>
    public double Jitter { get; set; }
    /// <summary>Chance from 0 to 1 that an unreliable packet is lost.</summary>
    public double Loss { get; set; }

    public bool IsActive => Latency > 0.0 || Jitter > 0.0 || Loss > 0.0;
    public int Pending => _queue.Count;

    public void Clear()
    {
        _queue.Clear();
        _lastReliableAt = 0.0;
    }

    /// <summary>Queues a packet sent at time <paramref name="now"/>. Returns false if it was lost.</summary>
    public bool Push(T packet, double now, bool reliable)
    {
        if (!reliable && Loss > 0.0 && _random.NextDouble() < Loss)
        {
            return false;
        }
        double deliverAt = now + Latency;
        if (Jitter > 0.0)
        {
            deliverAt += _random.NextDouble() * Jitter;
        }
        if (reliable)
        {
            deliverAt = Math.Max(deliverAt, _lastReliableAt);
            _lastReliableAt = deliverAt;
        }
        _queue.Add((deliverAt, _order++, packet));
        return true;
    }

    /// <summary>Removes and returns every packet due by <paramref name="now"/>, earliest first.</summary>
    public List<T> PopReady(double now)
    {
        var ready = new List<(double DeliverAt, long Order, T Packet)>();
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            if (_queue[i].DeliverAt <= now)
            {
                ready.Add(_queue[i]);
                _queue.RemoveAt(i);
            }
        }
        ready.Sort((a, b) => a.DeliverAt != b.DeliverAt
            ? a.DeliverAt.CompareTo(b.DeliverAt)
            : a.Order.CompareTo(b.Order));
        return ready.ConvertAll(entry => entry.Packet);
    }
}
