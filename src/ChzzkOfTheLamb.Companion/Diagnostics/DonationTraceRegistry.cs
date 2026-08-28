using System.Collections.Concurrent;
using System.Diagnostics;

namespace ChzzkOfTheLamb.Companion.Diagnostics;

internal sealed class DonationTraceRegistry
{
    private readonly ConcurrentDictionary<string, DonationTrace> _pending = new(StringComparer.Ordinal);

    public int PendingCount => _pending.Count;

    public DonationTrace Begin(
        string requestId,
        string source,
        string donorHash,
        long amount,
        string area,
        string tier,
        string effect,
        string eventName)
    {
        var trace = new DonationTrace(
            requestId,
            source,
            donorHash,
            amount,
            area,
            tier,
            effect,
            eventName,
            DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp());
        if (!_pending.TryAdd(requestId, trace))
            throw new InvalidOperationException($"Duplicate donation request ID: {requestId}");
        return trace;
    }

    public bool TryComplete(string requestId, out DonationTrace? trace, out double elapsedMs) =>
        TryRemove(requestId, out trace, out elapsedMs);

    public bool TryAbandon(string requestId, out DonationTrace? trace, out double elapsedMs) =>
        TryRemove(requestId, out trace, out elapsedMs);

    public bool Contains(string requestId) => _pending.ContainsKey(requestId);

    public int AbandonAll()
    {
        var abandoned = 0;
        foreach (var requestId in _pending.Keys)
        {
            if (_pending.TryRemove(requestId, out _)) abandoned++;
        }
        return abandoned;
    }

    private bool TryRemove(string requestId, out DonationTrace? trace, out double elapsedMs)
    {
        if (_pending.TryRemove(requestId, out var removed))
        {
            trace = removed;
            elapsedMs = (Stopwatch.GetTimestamp() - removed.StartedTimestamp) * 1000.0 / Stopwatch.Frequency;
            return true;
        }
        trace = null;
        elapsedMs = -1;
        return false;
    }
}

internal sealed record DonationTrace(
    string RequestId,
    string Source,
    string DonorHash,
    long Amount,
    string Area,
    string Tier,
    string Effect,
    string EventName,
    DateTimeOffset StartedAtUtc,
    long StartedTimestamp);
