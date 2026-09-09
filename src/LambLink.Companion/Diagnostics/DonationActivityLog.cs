using System.Text.Json;
using LambLink.Companion.Chzzk;

namespace LambLink.Companion.Diagnostics;

/// <summary>Bounded operational history, separate from the authoritative delivery outbox. No donor identity/message.</summary>
internal sealed class DonationActivityLog
{
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, DonationActivity> _items = new(StringComparer.Ordinal);
    private long _received, _parsed, _parseFailed, _unsupported;
    private string _historyError = "none";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DonationActivityLog(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
                _items = (JsonSerializer.Deserialize<List<DonationActivity>>(File.ReadAllText(path), JsonOptions) ?? [])
                    .Where(x => Guid.TryParseExact(x.Id, "N", out _)).GroupBy(x => x.Id)
                    .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
            Trim();
        }
        catch (Exception ex) { _historyError = ex.GetType().Name; }
    }

    public void Observe(DonationReception reception)
    {
        lock (_gate)
        {
            _received++;
            var result = reception.Result;
            if (result.Success) _parsed++;
            else if (result.Code == "UNSUPPORTED_DONATION_TYPE") _unsupported++;
            else _parseFailed++;
            long? amount = result.Donation?.TryGetAmount(out var parsed, out _) == true ? parsed : null;
            _items[reception.Id] = new(reception.Id, reception.ReceivedAtUtc, DateTimeOffset.UtcNow, amount,
                result.Success ? "PARSED" : result.Code == "UNSUPPORTED_DONATION_TYPE" ? "EXCLUDED" : "PARSE_FAILED",
                result.Code, "", "NOT_REQUESTED");
            SaveBestEffort();
        }
    }

    public void Recover(string id, DateTimeOffset receivedAt, long amount, string effect)
    {
        lock (_gate)
        {
            // Outbox is authoritative after a crash, including a crash between ACK/history writes.
            var previous = _items.GetValueOrDefault(id);
            _items[id] = new(id, receivedAt, DateTimeOffset.UtcNow, amount, "QUEUED", "RECOVERED", effect,
                previous?.Display ?? "NOT_REQUESTED");
            SaveBestEffort();
        }
    }

    public void Update(string id, string stage, string reason = "", string? effect = null)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(id, out var current)) return;
            // Duplicate/delayed transport callbacks must not regress a completed operation.
            if (Terminal(current.Stage) && !Terminal(stage)) return;
            _items[id] = current with { Stage = stage, Reason = reason, Effect = effect ?? current.Effect, UpdatedAtUtc = DateTimeOffset.UtcNow,
                AppliedAtUtc = stage == "APPLIED" ? current.AppliedAtUtc ?? DateTimeOffset.UtcNow : current.AppliedAtUtc };
            Console.WriteLine($"[DONATION][STATE] utc={DateTimeOffset.UtcNow:O}, request={id}, stage={stage}, reason={Safe(reason)}, effect={Safe(effect ?? current.Effect)}");
            SaveBestEffort();
        }
    }

    public void Display(string id, string state)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(id, out var current) || current.Display == state) return;
            _items[id] = current with { Display = state, UpdatedAtUtc = DateTimeOffset.UtcNow };
            Console.WriteLine($"[DONATION][DISPLAY] utc={DateTimeOffset.UtcNow:O}, request={id}, state={state}");
            SaveBestEffort();
        }
    }

    public string Summary()
    {
        lock (_gate)
        {
            var lastError = _items.Values.Where(x => x.Stage is "FAILED" or "PARSE_FAILED" or "UNCERTAIN")
                .OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault()?.Reason ?? "none";
            return $"DONATION_RECEIVED_SESSION={_received}, DONATION_PARSED_SESSION={_parsed}, DONATION_PARSE_FAILED_SESSION={_parseFailed}, DONATION_UNSUPPORTED_SESSION={_unsupported}, DONATION_RECENT={_items.Count}, DONATION_APPLIED_RECENT={_items.Values.Count(x => x.Stage == "APPLIED")}, DONATION_FAILED_RECENT={_items.Values.Count(x => x.Stage is "FAILED" or "PARSE_FAILED")}, DONATION_UNCERTAIN_RECENT={_items.Values.Count(x => x.Stage == "UNCERTAIN")}, DONATION_EXCLUDED_RECENT={_items.Values.Count(x => x.Stage == "EXCLUDED")}, DONATION_WAITING_CURRENT={_items.Values.Count(x => !Terminal(x.Stage))}, DONATION_LAST_ERROR={Safe(lastError)}, DONATION_HISTORY_ERROR={_historyError}";
        }
    }

    public string[] Recent(int count = 20)
    {
        lock (_gate)
            return _items.Values.OrderByDescending(x => x.ReceivedAtUtc).Take(count)
                .Select(x => $"utc={x.ReceivedAtUtc:O}, request={x.Id}, amount={x.Amount?.ToString() ?? "unknown"}, stage={x.Stage}, reason={Safe(x.Reason)}, effect={Safe(x.Effect)}, display={x.Display}").ToArray();
    }

    public void ReconcilePending(ISet<string> outboxIds)
    {
        lock (_gate)
        {
            foreach (var item in _items.Values.ToArray())
            {
                if (!Terminal(item.Stage) && !outboxIds.Contains(item.Id))
                    _items[item.Id] = item with { Stage = "UNCERTAIN", Reason = "RESTART_WITHOUT_DELIVERY_RECORD", UpdatedAtUtc = DateTimeOffset.UtcNow };
                else if (item.Stage == "APPLIED" && item.Display != "PAGE_CONFIRMED" && !item.Display.StartsWith("EXPIRED", StringComparison.Ordinal)
                    && (item.AppliedAtUtc ?? item.UpdatedAtUtc) <= DateTimeOffset.UtcNow.AddMinutes(-30))
                    _items[item.Id] = item with { Display = "EXPIRED_AGE", UpdatedAtUtc = DateTimeOffset.UtcNow };
            }
            SaveBestEffort();
        }
    }

    public DonationActivity[] PendingDisplays()
    {
        lock (_gate)
            return _items.Values.Where(x => x.Stage == "APPLIED" && x.Display != "PAGE_CONFIRMED" && !x.Display.StartsWith("EXPIRED", StringComparison.Ordinal)
                && (x.AppliedAtUtc ?? x.UpdatedAtUtc) > DateTimeOffset.UtcNow.AddMinutes(-30)).OrderBy(x => x.ReceivedAtUtc).ToArray();
    }

    private static bool Terminal(string stage) => stage is "APPLIED" or "FAILED" or "EXCLUDED" or "PARSE_FAILED" or "UNCERTAIN";
    private static string Safe(string text) => text.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(text.Length, 160)];
    private void Trim()
    {
        var expired = _items.Values.Where(x => Terminal(x.Stage)).OrderByDescending(x => x.UpdatedAtUtc)
            .Where((x, index) => index >= 1000 || x.UpdatedAtUtc < DateTimeOffset.UtcNow.AddDays(-30)).Select(x => x.Id).ToArray();
        foreach (var id in expired) _items.Remove(id);
    }
    private void SaveBestEffort()
    {
        Trim();
        try
        {
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_items.Values.ToArray(), JsonOptions));
            File.Move(_path + ".tmp", _path, overwrite: true);
            _historyError = "none";
        }
        catch (Exception ex)
        {
            if (_historyError != ex.GetType().Name) Console.Error.WriteLine($"[DONATION][HISTORY-SAVE-FAILED] type={ex.GetType().Name}");
            _historyError = ex.GetType().Name;
        }
    }
}

internal sealed record DonationActivity(string Id, DateTimeOffset ReceivedAtUtc, DateTimeOffset UpdatedAtUtc,
    long? Amount, string Stage, string Reason, string Effect, string Display, DateTimeOffset? AppliedAtUtc = null);
