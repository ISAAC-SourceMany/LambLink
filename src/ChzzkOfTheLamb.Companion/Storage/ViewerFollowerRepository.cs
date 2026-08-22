using System.Text.Json;

namespace ChzzkOfTheLamb.Companion.Storage;

public sealed class ViewerFollowerRecord
{
    public string StreamerChannelId { get; set; } = string.Empty;
    public string ViewerChannelId { get; set; } = string.Empty;
    public string LastKnownNickname { get; set; } = string.Empty;
    public string SaveId { get; set; } = string.Empty;
    public int FollowerId { get; set; }
    public bool IsAlive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DiedAt { get; set; }
    public string? DeathReason { get; set; }
}

public sealed class ViewerFollowerRepository
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<ViewerFollowerRecord> _records;
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
            if (string.Equals(normalized, record.LastKnownNickname, StringComparison.Ordinal)) continue;
            record.LastKnownNickname = normalized;
            migrated++;
        }

        if (migrated > 0)
        {
            Save();
            Console.WriteLine($"[FOLLOWER-MIGRATION] normalized {migrated} legacy CHZZK nickname record(s) in viewer-followers.json.");
        }
    }

    public bool Exists(string streamerChannelId, string viewerChannelId, string saveId)
    {
        lock (_gate)
            return _records.Any(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId && x.SaveId == saveId);
    }


    public ViewerFollowerRecord? Find(string streamerChannelId, string viewerChannelId, string saveId)
    {
        lock (_gate)
            return _records.FirstOrDefault(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId && x.SaveId == saveId);
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
                    SaveId = x.SaveId,
                    FollowerId = x.FollowerId,
                    IsAlive = x.IsAlive,
                    CreatedAt = x.CreatedAt,
                    DiedAt = x.DiedAt,
                    DeathReason = x.DeathReason
                }).ToList();
    }

    public void Upsert(ViewerFollowerRecord record)
    {
        lock (_gate)
        {
            var existing = _records.FirstOrDefault(x => x.StreamerChannelId == record.StreamerChannelId && x.ViewerChannelId == record.ViewerChannelId && x.SaveId == record.SaveId);
            if (existing is null) _records.Add(record);
            else
            {
                existing.LastKnownNickname = record.LastKnownNickname;
                existing.FollowerId = record.FollowerId;
                existing.IsAlive = record.IsAlive;
                existing.DiedAt = record.DiedAt;
                existing.DeathReason = record.DeathReason;
            }
            Save();
        }
    }

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
