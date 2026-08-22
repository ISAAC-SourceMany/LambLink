namespace ChzzkOfTheLamb.Companion.Raffle;

public sealed record RaffleEntry(string ViewerId, string Nickname, DateTimeOffset JoinedAt);

public sealed class RaffleManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RaffleEntry> _entries = new(StringComparer.Ordinal);
    private CancellationTokenSource? _roundCts;

    public bool IsOpen { get; private set; }
    public string? CurrentRaffleId { get; private set; }
    public int ParticipantCount { get { lock (_gate) return _entries.Count; } }

    public event Action<string, int>? Started;
    public event Action<int>? ParticipantCountChanged;
    public event Action<RaffleEntry?>? Completed;
    public event Action? Cancelled;

    public bool Join(string viewerId, string nickname)
    {
        lock (_gate)
        {
            if (!IsOpen || string.IsNullOrWhiteSpace(viewerId)) return false;
            if (_entries.ContainsKey(viewerId)) return false;
            _entries[viewerId] = new RaffleEntry(viewerId, nickname, DateTimeOffset.UtcNow);
        }

        ParticipantCountChanged?.Invoke(ParticipantCount);
        return true;
    }

    public async Task StartAsync(int durationSeconds, CancellationToken appCt)
    {
        CancellationToken token;
        string raffleId;
        lock (_gate)
        {
            if (IsOpen) return;
            IsOpen = true;
            CurrentRaffleId = Guid.NewGuid().ToString("N");
            raffleId = CurrentRaffleId;
            _entries.Clear();
            _roundCts = CancellationTokenSource.CreateLinkedTokenSource(appCt);
            token = _roundCts.Token;
        }

        Started?.Invoke(raffleId, durationSeconds);
        ParticipantCountChanged?.Invoke(0);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, durationSeconds)), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Complete();
    }

    public void Complete()
    {
        RaffleEntry? winner = null;
        lock (_gate)
        {
            if (!IsOpen) return;
            if (_entries.Count > 0)
            {
                var list = _entries.Values.ToArray();
                winner = list[Random.Shared.Next(list.Length)];
            }
            IsOpen = false;
            _roundCts?.Dispose();
            _roundCts = null;
        }
        Completed?.Invoke(winner);
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (!IsOpen) return;
            IsOpen = false;
            _roundCts?.Cancel();
            _roundCts?.Dispose();
            _roundCts = null;
            _entries.Clear();
        }
        Cancelled?.Invoke();
    }
}
