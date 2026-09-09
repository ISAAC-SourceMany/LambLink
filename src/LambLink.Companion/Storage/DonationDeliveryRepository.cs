using System.Text.Json;

namespace LambLink.Companion.Storage;

/// <summary>
/// Durable Companion-side outbox for real CHZZK donations. Records stay here until the
/// game Mod returns a terminal DONATION_EFFECT_RESULT, so a temporary game disconnect or
/// Companion restart does not silently discard a paid event.
/// </summary>
internal sealed class DonationDeliveryRepository
{
    private const int SchemaVersion = 1;
    private const int MaximumPendingDeliveries = 5_000;
    private readonly object _gate = new();
    private readonly string _path;
    private DonationDeliveryFile _state;
    public string RecoveryError { get; private set; } = "none";

    public DonationDeliveryRepository(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)
                                  ?? throw new ArgumentException("Donation outbox path has no directory.", nameof(path)));
        _state = Load(path);
    }

    public int PendingCount
    {
        get { lock (_gate) return _state.Deliveries.Count; }
    }

    public bool Contains(string requestId)
    {
        lock (_gate)
            return _state.Deliveries.Any(x => string.Equals(x.RequestId, requestId, StringComparison.Ordinal));
    }

    public bool TryAdd(DonationDeliveryRecord delivery)
    {
        if (string.IsNullOrWhiteSpace(delivery.RequestId))
            throw new ArgumentException("Donation request ID is required.", nameof(delivery));

        lock (_gate)
        {
            EnsureAvailable();
            if (_state.Deliveries.Any(x => string.Equals(x.RequestId, delivery.RequestId, StringComparison.Ordinal)))
                return false;
            if (_state.Deliveries.Count >= MaximumPendingDeliveries)
                throw new InvalidOperationException($"Donation outbox limit reached ({MaximumPendingDeliveries}).");

            var next = CopyState();
            next.Deliveries.Add(Clone(delivery));
            SaveLocked(next);
            return true;
        }
    }

    public IReadOnlyList<DonationDeliveryRecord> Snapshot()
    {
        lock (_gate)
            return _state.Deliveries
                .OrderBy(x => x.ReceivedAtUtc)
                .ThenBy(x => x.RequestId, StringComparer.Ordinal)
                .Select(Clone)
                .ToArray();
    }

    public bool TryRecordAttempt(string requestId, DateTimeOffset attemptedAtUtc)
    {
        lock (_gate)
        {
            EnsureAvailable();
            var next = CopyState();
            var delivery = next.Deliveries.FirstOrDefault(x =>
                string.Equals(x.RequestId, requestId, StringComparison.Ordinal));
            if (delivery is null) return false;
            delivery.AttemptCount++;
            delivery.LastAttemptAtUtc = attemptedAtUtc;
            SaveLocked(next);
            return true;
        }
    }

    public bool TryComplete(string requestId, out DonationDeliveryRecord? delivery)
    {
        lock (_gate)
        {
            EnsureAvailable();
            var index = _state.Deliveries.FindIndex(x =>
                string.Equals(x.RequestId, requestId, StringComparison.Ordinal));
            if (index < 0)
            {
                delivery = null;
                return false;
            }

            delivery = Clone(_state.Deliveries[index]);
            var next = CopyState();
            next.Deliveries.RemoveAt(index);
            SaveLocked(next);
            return true;
        }
    }

    private DonationDeliveryFile Load(string path)
    {
        if (!File.Exists(path)) return new DonationDeliveryFile { Version = SchemaVersion };
        try
        {
            var state = JsonSerializer.Deserialize<DonationDeliveryFile>(File.ReadAllText(path), JsonOptions)
                        ?? throw new InvalidDataException("Empty donation outbox.");
            if (state.Version != SchemaVersion) throw new InvalidDataException("Unsupported donation outbox version.");
            state.Deliveries ??= new List<DonationDeliveryRecord>();
            if (state.Deliveries.Count > MaximumPendingDeliveries || state.Deliveries.Any(x => string.IsNullOrWhiteSpace(x.RequestId)))
                throw new InvalidDataException("Invalid donation outbox entries.");
            if (state.Deliveries.Select(x => x.RequestId).Distinct(StringComparer.Ordinal).Count() != state.Deliveries.Count)
                throw new InvalidDataException("Duplicate donation outbox entries.");
            state.Deliveries = state.Deliveries
                .Where(x => !string.IsNullOrWhiteSpace(x.RequestId))
                .GroupBy(x => x.RequestId, StringComparer.Ordinal)
                .Select(x => x.OrderBy(y => y.ReceivedAtUtc).First())
                .Take(MaximumPendingDeliveries)
                .ToList();
            return state;
        }
        catch (Exception ex)
        {
            RecoveryError = ex.GetType().Name;
            Console.Error.WriteLine($"[DONATION][OUTBOX][RECOVERY-BLOCKED] original file preserved; type={RecoveryError}; repair required before accepting deliveries");
            return new DonationDeliveryFile { Version = SchemaVersion };
        }
    }

    private void EnsureAvailable()
    {
        if (RecoveryError != "none") throw new InvalidOperationException("Donation outbox requires recovery.");
    }

    private DonationDeliveryFile CopyState() => new() { Version = SchemaVersion, Deliveries = _state.Deliveries.Select(Clone).ToList() };

    private void SaveLocked(DonationDeliveryFile next)
    {
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(next, JsonOptions));
        if (File.Exists(_path))
        {
            try { File.Replace(tempPath, _path, destinationBackupFileName: null); }
            catch (PlatformNotSupportedException)
            {
                File.Move(tempPath, _path, overwrite: true);
            }
        }
        else
        {
            File.Move(tempPath, _path);
        }
        _state = next;
    }

    private static DonationDeliveryRecord Clone(DonationDeliveryRecord source) => new()
    {
        RequestId = source.RequestId,
        Source = source.Source,
        DonorHash = source.DonorHash,
        ViewerId = source.ViewerId,
        Nickname = source.Nickname,
        Amount = source.Amount,
        Area = source.Area,
        TierName = source.TierName,
        Effect = source.Effect,
        EventName = source.EventName,
        ReceivedAtUtc = source.ReceivedAtUtc,
        AttemptCount = source.AttemptCount,
        LastAttemptAtUtc = source.LastAttemptAtUtc
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed class DonationDeliveryFile
    {
        public int Version { get; set; } = SchemaVersion;
        public List<DonationDeliveryRecord> Deliveries { get; set; } = new();
    }
}

internal sealed class DonationDeliveryRecord
{
    public string RequestId { get; set; } = string.Empty;
    public string Source { get; set; } = "CHZZK";
    public string DonorHash { get; set; } = "none";
    public string ViewerId { get; set; } = string.Empty;
    public string Nickname { get; set; } = "후원자";
    public long Amount { get; set; }
    public string Area { get; set; } = "UNKNOWN";
    public string TierName { get; set; } = string.Empty;
    public string Effect { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
}
