using System.Text.Json;
using ChzzkOfTheLamb.Protocol;

namespace ChzzkOfTheLamb.Companion.Appearance;

public sealed class ViewerAppearanceRecord
{
    public string StreamerChannelId { get; set; } = string.Empty;
    public string ViewerChannelId { get; set; } = string.Empty;
    public FollowerAppearanceSelection Appearance { get; set; } = new();
}

public sealed class AppearanceStoreState
{
    public List<ViewerAppearanceRecord> ViewerAppearances { get; set; } = new();
    public List<string> AllowedFormIds { get; set; } = new();
    public List<string> DeniedFormIds { get; set; } = new();
    public List<string> KnownUnlockedFormIds { get; set; } = new();
    public bool AllowListInitialized { get; set; }
    public int CatalogPolicyVersion { get; set; }
}

public sealed class AppearanceStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly AppearanceStoreState _state;
    private readonly HashSet<string> _allowedFormIds;
    private readonly HashSet<string> _deniedFormIds;
    private readonly HashSet<string> _knownUnlockedFormIds;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const int CurrentCatalogPolicyVersion = 2;

    public FollowerAppearanceCatalog? LatestCatalog { get; private set; }

    public AppearanceStore(string path)
    {
        _path = path;
        _state = File.Exists(path)
            ? JsonSerializer.Deserialize<AppearanceStoreState>(File.ReadAllText(path), Json) ?? new()
            : new();
        _allowedFormIds = new HashSet<string>(_state.AllowedFormIds ?? new(), StringComparer.Ordinal);
        _deniedFormIds = new HashSet<string>(_state.DeniedFormIds ?? new(), StringComparer.Ordinal);
        _knownUnlockedFormIds = new HashSet<string>(_state.KnownUnlockedFormIds ?? new(), StringComparer.Ordinal);
    }

    public IReadOnlyList<string> UpdateCatalog(FollowerAppearanceCatalog catalog)
    {
        lock (_gate)
        {
            LatestCatalog = catalog;
            if (catalog.Forms.Count == 0) return Array.Empty<string>();

            var eligible = catalog.Forms
                .Where(x => x.IsUnlocked && !x.IsSpecial && !string.IsNullOrWhiteSpace(x.FormId))
                .Select(x => x.FormId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            var changed = false;
            var newlyAllowed = new List<string>();

            if (_state.CatalogPolicyVersion < CurrentCatalogPolicyVersion)
            {
                // RC17 and older did not remember explicit denials separately. Preserve the
                // streamer's existing choices during the one-time migration: a currently
                // unlocked form missing from a non-empty legacy allow-list is treated as an
                // intentional denial. A legacy empty list is repaired by allowing all normal
                // unlocked forms, matching the previous corruption-recovery behavior.
                if (_state.AllowListInitialized && _allowedFormIds.Count > 0)
                {
                    foreach (var formId in eligible)
                        if (!_allowedFormIds.Contains(formId))
                            changed |= _deniedFormIds.Add(formId);
                }
                else
                {
                    foreach (var formId in eligible)
                        if (!_deniedFormIds.Contains(formId))
                            changed |= _allowedFormIds.Add(formId);
                }

                foreach (var formId in eligible)
                    changed |= _knownUnlockedFormIds.Add(formId);
                _state.AllowListInitialized = true;
                _state.CatalogPolicyVersion = CurrentCatalogPolicyVersion;
                changed = true;
            }
            else
            {
                // From RC18 onward, every genuinely new normal unlocked form becomes selectable
                // automatically unless the streamer explicitly denied that form in the past.
                foreach (var formId in eligible)
                {
                    if (!_knownUnlockedFormIds.Add(formId)) continue;
                    changed = true;
                    if (_deniedFormIds.Contains(formId)) continue;
                    if (_allowedFormIds.Add(formId)) newlyAllowed.Add(formId);
                }
            }

            // Denial always wins if an older/corrupt state contains the ID in both collections.
            foreach (var formId in _deniedFormIds)
                changed |= _allowedFormIds.Remove(formId);

            if (changed) PersistLocked();
            return newlyAllowed;
        }
    }

    public IReadOnlyCollection<string> AllowedFormIds
    {
        get { lock (_gate) return _allowedFormIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(); }
    }

    public void SetFormAllowed(string formId, bool allowed)
    {
        if (string.IsNullOrWhiteSpace(formId)) return;
        lock (_gate)
        {
            if (allowed)
            {
                _deniedFormIds.Remove(formId);
                _allowedFormIds.Add(formId);
            }
            else
            {
                _allowedFormIds.Remove(formId);
                _deniedFormIds.Add(formId);
            }
            _knownUnlockedFormIds.Add(formId);
            _state.AllowListInitialized = true;
            _state.CatalogPolicyVersion = CurrentCatalogPolicyVersion;
            PersistLocked();
        }
    }

    public FollowerAppearanceSelection? Get(string streamerChannelId, string viewerChannelId)
    {
        lock (_gate)
            return _state.ViewerAppearances.FirstOrDefault(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId)?.Appearance;
    }

    public bool Validate(FollowerAppearanceSelection appearance)
    {
        lock (_gate)
        {
            var form = LatestCatalog?.Forms.FirstOrDefault(x => x.FormId == appearance.FormId);
            if (form is null || !form.IsUnlocked || !_allowedFormIds.Contains(form.FormId)) return false;
            if (appearance.VariantId is not null && form.VariantIds.Count > 0 && !form.VariantIds.Contains(appearance.VariantId)) return false;
            if (appearance.ColorId is not null && form.ColorIds.Count > 0 && !form.ColorIds.Contains(appearance.ColorId)) return false;
            return true;
        }
    }

    public void SaveViewerAppearance(string streamerChannelId, string viewerChannelId, FollowerAppearanceSelection appearance)
    {
        if (!Validate(appearance)) throw new InvalidOperationException("Appearance is not allowed by the current game catalog/streamer filter.");
        lock (_gate)
        {
            var existing = _state.ViewerAppearances.FirstOrDefault(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId);
            if (existing is null)
                _state.ViewerAppearances.Add(new ViewerAppearanceRecord { StreamerChannelId = streamerChannelId, ViewerChannelId = viewerChannelId, Appearance = appearance });
            else existing.Appearance = appearance;
            PersistLocked();
        }
    }

    private void PersistLocked()
    {
        _state.AllowedFormIds = _allowedFormIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
        _state.DeniedFormIds = _deniedFormIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
        _state.KnownUnlockedFormIds = _knownUnlockedFormIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(_state, Json));
    }
}
