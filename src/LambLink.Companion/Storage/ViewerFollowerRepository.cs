using System.Text.Json;

namespace LambLink.Companion.Storage;

public sealed class ViewerFollowerRecord
{
    public string StreamerChannelId { get; set; } = string.Empty;
    public string ViewerChannelId { get; set; } = string.Empty;
    public string LastKnownNickname { get; set; } = string.Empty;
    public string FollowerName { get; set; } = string.Empty;
    public string SaveId { get; set; } = string.Empty;
    public int FollowerId { get; set; }
    public int Generation { get; set; } = 1;
    public bool IsAlive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DiedAt { get; set; }
    public string? DeathReason { get; set; }
    public DateTimeOffset? ResurrectedAt { get; set; }
    public LambLink.Protocol.FollowerAppearanceSelection? Appearance { get; set; }
    public List<ViewerFollowerHistoryEvent> Events { get; set; } = new();
    public long Revision { get; set; }
}

public sealed record ViewerFollowerStateSnapshot(
    long Revision,
    bool CanCreate,
    int NextGeneration,
    IReadOnlyList<ViewerFollowerRecord> History);

public sealed class ViewerFollowerHistoryEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string? Cause { get; set; }
}

public sealed class ViewerFollowerRepository
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<ViewerFollowerRecord> _records;
    private long _nextRevision;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ViewerFollowerRepository(string path)
    {
        _path = path;
        _records = File.Exists(path)
            ? JsonSerializer.Deserialize<List<ViewerFollowerRecord>>(File.ReadAllText(path), Json) ?? new()
            : new();

        // devbridge10n migration: old builds persisted the visual CHZZK rich-text prefix
        // inside LastKnownNickname. Normalize the data once on repository load and rewrite
        // viewer-followers.json immediately so all future comparisons use the plain nickname.
        var migrated = 0;
        foreach (var record in _records)
        {
            var normalized = NormalizeLegacyChzzkName(record.LastKnownNickname);
            if (!string.Equals(normalized, record.LastKnownNickname, StringComparison.Ordinal)) { record.LastKnownNickname = normalized; migrated++; }
            if (string.IsNullOrWhiteSpace(record.FollowerName)) { record.FollowerName = BuildFollowerName(normalized, Math.Max(1, record.Generation)); migrated++; }
            if (record.Generation < 1) { record.Generation = 1; migrated++; }
            if (record.Events.Count == 0)
            {
                record.Events.Add(new ViewerFollowerHistoryEvent { Type = "Created", At = record.CreatedAt });
                if (record.DiedAt.HasValue) record.Events.Add(new ViewerFollowerHistoryEvent { Type = "Died", At = record.DiedAt.Value, Cause = record.DeathReason });
                migrated++;
            }
            if (record.Revision <= 0) { record.Revision = ++_nextRevision; migrated++; }
            else _nextRevision = Math.Max(_nextRevision, record.Revision);
        }

        if (migrated > 0)
        {
            Save();
            Console.WriteLine($"[FOLLOWER-MIGRATION] normalized {migrated} legacy CHZZK nickname record(s) in viewer-followers.json.");
        }
    }

    public bool Exists(string streamerChannelId, string viewerChannelId, string saveId)
    {
        lock (_gate) return !CanCreateLocked(streamerChannelId, viewerChannelId, saveId);
    }


    public ViewerFollowerRecord? Find(string streamerChannelId, string viewerChannelId, string saveId)
    {
        lock (_gate)
            return _records.Where(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId && x.SaveId == saveId).OrderByDescending(x => x.Generation).FirstOrDefault();
    }

    public bool Remove(string streamerChannelId, string viewerChannelId, string saveId)
    {
        lock (_gate)
        {
            var removed = _records.RemoveAll(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId && x.SaveId == saveId) > 0;
            if (removed) Save();
            return removed;
        }
    }

    public IReadOnlyList<ViewerFollowerRecord> GetForSave(string streamerChannelId, string saveId)
    {
        lock (_gate)
            return _records.Where(x => x.StreamerChannelId == streamerChannelId && x.SaveId == saveId)
                .Select(x => new ViewerFollowerRecord
                {
                    StreamerChannelId = x.StreamerChannelId,
                    ViewerChannelId = x.ViewerChannelId,
                    LastKnownNickname = x.LastKnownNickname,
                    FollowerName = x.FollowerName,
                    SaveId = x.SaveId,
                    FollowerId = x.FollowerId,
                    Generation = x.Generation,
                    IsAlive = x.IsAlive,
                    CreatedAt = x.CreatedAt,
                    DiedAt = x.DiedAt,
                    DeathReason = x.DeathReason
                    ,ResurrectedAt = x.ResurrectedAt, Appearance = x.Appearance, Events = x.Events.ToList(), Revision = x.Revision
                }).ToList();
    }

    public IReadOnlyList<ViewerFollowerRecord> GetForSaveAnyStreamer(string saveId)
    {
        lock (_gate)
            return _records.Where(x => x.SaveId == saveId)
                .Select(x => new ViewerFollowerRecord
                {
                    StreamerChannelId = x.StreamerChannelId,
                    ViewerChannelId = x.ViewerChannelId,
                    LastKnownNickname = x.LastKnownNickname,
                    FollowerName = x.FollowerName,
                    SaveId = x.SaveId,
                    FollowerId = x.FollowerId,
                    Generation = x.Generation,
                    IsAlive = x.IsAlive,
                    CreatedAt = x.CreatedAt,
                    DiedAt = x.DiedAt,
                    DeathReason = x.DeathReason
                    ,ResurrectedAt = x.ResurrectedAt, Appearance = x.Appearance, Events = x.Events.ToList(), Revision = x.Revision
                }).ToList();
    }

    public void Upsert(ViewerFollowerRecord record)
    {
        lock (_gate)
        {
            if (record.Generation < 1)
                record.Generation = _records.Where(x => x.StreamerChannelId == record.StreamerChannelId && x.ViewerChannelId == record.ViewerChannelId && x.SaveId == record.SaveId).Select(x => x.Generation).DefaultIfEmpty(0).Max() + 1;
            if (string.IsNullOrWhiteSpace(record.FollowerName)) record.FollowerName = BuildFollowerName(record.LastKnownNickname, record.Generation);
            if (record.Events.Count == 0) record.Events.Add(new ViewerFollowerHistoryEvent { Type = "Created", At = record.CreatedAt });
            record.Revision = ++_nextRevision;
            var existing = _records.FirstOrDefault(x => x.StreamerChannelId == record.StreamerChannelId && x.ViewerChannelId == record.ViewerChannelId && x.SaveId == record.SaveId && x.Generation == record.Generation);
            if (existing is null) _records.Add(record);
            else
            {
                existing.LastKnownNickname = record.LastKnownNickname;
                existing.FollowerName = record.FollowerName;
                existing.FollowerId = record.FollowerId;
                existing.Appearance = record.Appearance;
                existing.Revision = record.Revision;
            }
            Save();
        }
    }

    public bool ReplaceStateIfNewer(
        string streamerChannelId,
        string viewerChannelId,
        string saveId,
        long revision,
        IReadOnlyCollection<ViewerFollowerRecord> records)
    {
        if (string.IsNullOrWhiteSpace(streamerChannelId) || string.IsNullOrWhiteSpace(viewerChannelId) ||
            string.IsNullOrWhiteSpace(saveId) || saveId == "unknown" || revision <= 0 || records.Count == 0)
            return false;

        lock (_gate)
        {
            var currentRevision = _records
                .Where(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId && x.SaveId == saveId)
                .Select(x => x.Revision)
                .DefaultIfEmpty(0)
                .Max();
            if (revision <= currentRevision) return false;

            var replacements = records
                .Where(x => x.FollowerId > 0 && !string.IsNullOrWhiteSpace(x.FollowerName))
                .GroupBy(x => Math.Max(1, x.Generation))
                .Select(g => Clone(g.OrderByDescending(x => x.CreatedAt).First()))
                .OrderBy(x => x.Generation)
                .ToList();
            foreach (var replacement in replacements)
            {
                replacement.StreamerChannelId = streamerChannelId;
                replacement.ViewerChannelId = viewerChannelId;
                replacement.SaveId = saveId;
                replacement.Generation = Math.Max(1, replacement.Generation);
                replacement.LastKnownNickname = NormalizeLegacyChzzkName(replacement.LastKnownNickname);
                replacement.FollowerName = NormalizeLegacyChzzkName(replacement.FollowerName);
                replacement.Revision = revision;
                if (replacement.Events.Count == 0)
                    replacement.Events.Add(new ViewerFollowerHistoryEvent { Type = "Created", At = replacement.CreatedAt });
            }

            _records.RemoveAll(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId && x.SaveId == saveId);
            _records.AddRange(replacements);
            _nextRevision = Math.Max(_nextRevision, revision);
            Save();
            return true;
        }
    }

    public int NextGeneration(string streamerChannelId, string viewerChannelId, string saveId)
    {
        lock (_gate) return _records.Where(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId && x.SaveId == saveId).Select(x => x.Generation).DefaultIfEmpty(0).Max() + 1;
    }

    public bool CanCreate(string streamerChannelId, string viewerChannelId, string saveId)
    {
        lock (_gate) return CanCreateLocked(streamerChannelId, viewerChannelId, saveId);
    }

    private bool CanCreateLocked(string streamerChannelId, string viewerChannelId, string saveId)
    {
        var latest = _records.Where(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId && x.SaveId == saveId)
            .OrderByDescending(x => x.Generation).FirstOrDefault();
        // Death permanently spends this generation and opens exactly one next generation.
        // A later resurrection changes its current life state but never revokes that entitlement.
        return latest is null || latest.DiedAt.HasValue;
    }

    public bool ApplyLifecycle(string streamerChannelId, string eventId, string saveId, int followerId, string type, DateTimeOffset at, string? cause)
    {
        lock (_gate)
        {
            var record = _records.FirstOrDefault(x => x.StreamerChannelId == streamerChannelId && x.SaveId == saveId && x.FollowerId == followerId);
            if (record is null || record.Events.Any(x => x.EventId == eventId)) return false;
            if (type == "Died") { record.IsAlive = false; record.DiedAt = at; record.DeathReason = cause; }
            else if (type == "Resurrected") { record.IsAlive = true; record.ResurrectedAt = at; }
            else return false;
            record.Events.Add(new ViewerFollowerHistoryEvent { EventId = eventId, Type = type, At = at, Cause = cause });
            record.Revision = ++_nextRevision;
            Save();
            return true;
        }
    }

    public ViewerFollowerStateSnapshot GetStateSnapshot(string streamerChannelId, string viewerChannelId, string saveId)
    {
        lock (_gate)
        {
            var records = _records.Where(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId && x.SaveId == saveId)
                .OrderBy(x => x.Generation).Select(Clone).ToList();
            var latest = records.OrderByDescending(x => x.Generation).FirstOrDefault();
            return new ViewerFollowerStateSnapshot(
                records.Select(x => x.Revision).DefaultIfEmpty(0).Max(),
                latest is null || latest.DiedAt.HasValue,
                records.Select(x => x.Generation).DefaultIfEmpty(0).Max() + 1,
                records);
        }
    }

    private static ViewerFollowerRecord Clone(ViewerFollowerRecord x) => new()
    {
        StreamerChannelId = x.StreamerChannelId, ViewerChannelId = x.ViewerChannelId,
        LastKnownNickname = x.LastKnownNickname, FollowerName = x.FollowerName, SaveId = x.SaveId,
        FollowerId = x.FollowerId, Generation = x.Generation, IsAlive = x.IsAlive,
        CreatedAt = x.CreatedAt, DiedAt = x.DiedAt, DeathReason = x.DeathReason,
        ResurrectedAt = x.ResurrectedAt, Appearance = x.Appearance, Events = x.Events.ToList(), Revision = x.Revision
    };

    public static string BuildFollowerName(string nickname, int generation)
        => generation <= 1 ? nickname.Trim() : $"{nickname.Trim()} {generation}세";

    private static string NormalizeLegacyChzzkName(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        const string legacyPrefix = "<color=#00C471>Chzzk</color> ";
        if (normalized.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(legacyPrefix.Length).Trim();
        return normalized;
    }

    private void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(_records, Json));
}
