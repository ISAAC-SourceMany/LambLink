using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using LambLink.Protocol;
using Newtonsoft.Json;

namespace LambLink.Mod.Game;

/// <summary>
/// Persists recent donation request outcomes so Companion retries can be answered without
/// applying the same gameplay mutation twice. An APPLYING record left by a terminated game
/// is resolved as uncertain and is deliberately not replayed.
/// </summary>
internal sealed class DonationReceiptStore
{
    private const int SchemaVersion = 2;
    private const int MaximumTerminalReceipts = 2_048;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly ManualLogSource _log;
    private DonationReceiptFile _state;
    private string _committedJson;
    private readonly Dictionary<string, DonationEffectResult> _unsavedResults = new(StringComparer.Ordinal);

    public DonationReceiptStore(string path, ManualLogSource log)
    {
        _path = path;
        _log = log;
        Directory.CreateDirectory(Path.GetDirectoryName(path)
                                  ?? throw new ArgumentException("Donation receipt path has no directory.", nameof(path)));
        _state = Load(path);
        _committedJson = JsonConvert.SerializeObject(_state);
        RecoverInterruptedApplications();
    }

    public bool TryGetTerminalResult(string requestId, out DonationEffectResult result)
    {
        lock (_gate)
        {
            var receipt = FindLocked(requestId);
            if (_unsavedResults.TryGetValue(requestId, out var unsaved))
            {
                result = Clone(unsaved);
                return true;
            }
            if (receipt?.Result != null &&
                (string.Equals(receipt.Status, "COMPLETED", StringComparison.Ordinal) ||
                 string.Equals(receipt.Status, "UNCERTAIN", StringComparison.Ordinal)))
            {
                result = Clone(receipt.Result);
                return true;
            }
        }

        result = null!;
        return false;
    }

    public void MarkPending(DonationEffectCommand command)
    {
        lock (_gate)
        {
            var receipt = FindLocked(command.RequestId);
            if (receipt == null)
            {
                if (_state.ReplayCutoffUnixMs > 0 && command.ReceivedAtUnixMs <= _state.ReplayCutoffUnixMs)
                    throw new DonationHistoryExpiredException();
                receipt = new DonationReceiptRecord { RequestId = command.RequestId };
                _state.Receipts.Add(receipt);
            }
            if (receipt.Result != null) return;

            receipt.Status = "PENDING";
            receipt.UpdatedAtUtc = DateTimeOffset.UtcNow;
            receipt.Command = Clone(command);
            SaveLocked();
        }
    }

    public void MarkApplying(DonationEffectCommand command)
    {
        lock (_gate)
        {
            var receipt = FindLocked(command.RequestId)
                          ?? throw new InvalidOperationException("Donation receipt is missing before apply.");
            if (receipt.Result != null) return;

            receipt.Status = "APPLYING";
            receipt.UpdatedAtUtc = DateTimeOffset.UtcNow;
            receipt.Command = Clone(command);
            SaveLocked();
        }
    }

    public void MarkCompleted(DonationEffectResult result)
    {
        lock (_gate)
        {
            // Preserve the applied result even when the disk commit fails.
            _unsavedResults[result.RequestId] = Clone(result);
            var receipt = FindLocked(result.RequestId);
            if (receipt == null)
            {
                receipt = new DonationReceiptRecord { RequestId = result.RequestId };
                _state.Receipts.Add(receipt);
            }

            receipt.Status = "COMPLETED";
            receipt.UpdatedAtUtc = DateTimeOffset.UtcNow;
            receipt.Result = Clone(result);
            TrimLocked();
            SaveLocked();
            _unsavedResults.Remove(result.RequestId);
        }
    }

    private void RecoverInterruptedApplications()
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var receipt in _state.Receipts.Where(x =>
                         string.Equals(x.Status, "APPLYING", StringComparison.Ordinal)))
            {
                var command = receipt.Command;
                receipt.Status = "UNCERTAIN";
                receipt.UpdatedAtUtc = DateTimeOffset.UtcNow;
                receipt.Result = new DonationEffectResult
                {
                    RequestId = receipt.RequestId,
                    Success = false,
                    StatusCode = "APPLICATION_UNCERTAIN",
                    Effect = command?.Effect ?? string.Empty,
                    EventName = command?.EventName ?? string.Empty,
                    Amount = command?.Amount ?? 0,
                    Nickname = command?.Nickname ?? string.Empty,
                    Error = "Previous game process ended while this donation was applying. It was not replayed to prevent a duplicate game effect."
                };
                changed = true;
                _log.LogWarning($"[DONATION][RECEIPT][UNCERTAIN] request={Short(receipt.RequestId)}; automatic replay blocked");
            }

            if (changed)
            {
                TrimLocked();
                SaveLocked();
            }
        }
    }

    private DonationReceiptFile Load(string path)
    {
        if (!File.Exists(path)) return new DonationReceiptFile { Version = SchemaVersion };
        try
        {
            var state = JsonConvert.DeserializeObject<DonationReceiptFile>(File.ReadAllText(path))
                        ?? throw new InvalidDataException("Empty donation receipts.");
            if (state.Version != 1 && state.Version != SchemaVersion) throw new InvalidDataException("Unsupported donation receipt version.");
            if (state.Receipts == null || state.Receipts.Any(x => string.IsNullOrWhiteSpace(x.RequestId)) ||
                state.Receipts.Select(x => x.RequestId).Distinct(StringComparer.Ordinal).Count() != state.Receipts.Count)
                throw new InvalidDataException("Invalid donation receipt entries.");
            if (state.Version == 1 && !File.Exists(path + ".v1.bak")) File.Copy(path, path + ".v1.bak");
            state.Receipts = (state.Receipts ?? new List<DonationReceiptRecord>())
                .Where(x => !string.IsNullOrWhiteSpace(x.RequestId))
                .GroupBy(x => x.RequestId, StringComparer.Ordinal)
                .Select(x => x.OrderByDescending(y => y.UpdatedAtUtc).First())
                .ToList();
            return state;
        }
        catch (Exception ex)
        {
            _log.LogError($"[DONATION][RECEIPT][RECOVERY-BLOCKED] original file preserved; type={ex.GetType().FullName}");
            throw new InvalidDataException("Donation receipts require recovery; automatic application disabled.", ex);
        }
    }

    private void SaveLocked()
    {
        try
        {
            _state.Version = SchemaVersion;
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonConvert.SerializeObject(_state, Formatting.Indented));
            if (File.Exists(_path))
            {
                try { File.Replace(tempPath, _path, null); }
                catch (PlatformNotSupportedException)
                {
                    throw new IOException("Atomic receipt replacement is unavailable.");
                }
            }
            else
            {
                File.Move(tempPath, _path);
            }
            _committedJson = JsonConvert.SerializeObject(_state);
        }
        catch
        {
            _state = JsonConvert.DeserializeObject<DonationReceiptFile>(_committedJson)!;
            throw;
        }
    }

    private DonationReceiptRecord? FindLocked(string requestId) =>
        _state.Receipts.FirstOrDefault(x => string.Equals(x.RequestId, requestId, StringComparison.Ordinal));

    private void TrimLocked()
    {
        var terminal = _state.Receipts
            .Where(x => x.Result != null)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Skip(MaximumTerminalReceipts)
            .ToArray();
        foreach (var receipt in terminal)
        {
            // A pruned request must never silently become a new game effect.
            var receivedAt = receipt.Command?.ReceivedAtUnixMs ?? 0;
            _state.ReplayCutoffUnixMs = Math.Max(_state.ReplayCutoffUnixMs,
                receivedAt > 0 ? receivedAt : receipt.UpdatedAtUtc.ToUnixTimeMilliseconds());
            _state.Receipts.Remove(receipt);
        }
    }

    private static DonationEffectCommand Clone(DonationEffectCommand source) => new()
    {
        ReceivedAtUnixMs = source.ReceivedAtUnixMs,
        RequestId = source.RequestId,
        ViewerId = source.ViewerId,
        Nickname = source.Nickname,
        Amount = source.Amount,
        Effect = source.Effect,
        EventName = source.EventName,
        Message = null
    };

    private static DonationEffectResult Clone(DonationEffectResult source) => new()
    {
        StatusCode = source.StatusCode,
        RequestId = source.RequestId,
        Success = source.Success,
        Retryable = source.Retryable,
        Effect = source.Effect,
        EventName = source.EventName,
        Amount = source.Amount,
        Nickname = source.Nickname,
        Details = source.Details,
        Error = source.Error
    };

    private static string Short(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value!.Substring(0, Math.Min(8, value.Length));

    private sealed class DonationReceiptFile
    {
        public int Version { get; set; } = SchemaVersion;
        public long ReplayCutoffUnixMs { get; set; }
        public List<DonationReceiptRecord> Receipts { get; set; } = new();
    }

    private sealed class DonationReceiptRecord
    {
        public string RequestId { get; set; } = string.Empty;
        public string Status { get; set; } = "PENDING";
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DonationEffectCommand? Command { get; set; }
        public DonationEffectResult? Result { get; set; }
    }
}

internal sealed class DonationHistoryExpiredException : InvalidOperationException
{
    public DonationHistoryExpiredException() : base("Donation predates retained replay history; application is unconfirmed and automatic replay is blocked.") { }
}
