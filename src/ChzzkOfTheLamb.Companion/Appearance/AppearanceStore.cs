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
    public bool AllowListInitialized { get; set; }
}

public sealed class AppearanceStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly AppearanceStoreState _state;
    private readonly HashSet<string> _allowedFormIds;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FollowerAppearanceCatalog? LatestCatalog { get; private set; }

    public AppearanceStore(string path)
    {
        _path = path;
        _state = File.Exists(path)
            ? JsonSerializer.Deserialize<AppearanceStoreState>(File.ReadAllText(path), Json) ?? new()
            : new();
        _allowedFormIds = new HashSet<string>(_state.AllowedFormIds, StringComparer.Ordinal);
    }

    public void UpdateCatalog(FollowerAppearanceCatalog catalog)
    {
        LatestCatalog = catalog;
        // Do not permanently initialize from the empty catalog seen on the main menu.
        // Also repairs older devbridge runs that persisted AllowListInitialized=true with 0 forms.
        if (catalog.Forms.Count > 0 && (!_state.AllowListInitialized || _allowedFormIds.Count == 0))
        {
            foreach (var form in catalog.Forms.Where(x => x.IsUnlocked && !x.IsSpecial))
                _allowedFormIds.Add(form.FormId);
            _state.AllowListInitialized = true;
            Persist();
        }
    }

    public IReadOnlyCollection<string> AllowedFormIds => _allowedFormIds;

    public void SetFormAllowed(string formId, bool allowed)
    {
        if (allowed) _allowedFormIds.Add(formId);
        else _allowedFormIds.Remove(formId);
        Persist();
    }

    public FollowerAppearanceSelection? Get(string streamerChannelId, string viewerChannelId)
    {
        lock (_gate)
            return _state.ViewerAppearances.FirstOrDefault(x => x.StreamerChannelId == streamerChannelId && x.ViewerChannelId == viewerChannelId)?.Appearance;
    }

    public bool Validate(FollowerAppearanceSelection appearance)
    {
        var form = LatestCatalog?.Forms.FirstOrDefault(x => x.FormId == appearance.FormId);
        if (form is null || !form.IsUnlocked || !_allowedFormIds.Contains(form.FormId)) return false;
        if (appearance.VariantId is not null && form.VariantIds.Count > 0 && !form.VariantIds.Contains(appearance.VariantId)) return false;
        if (appearance.ColorId is not null && form.ColorIds.Count > 0 && !form.ColorIds.Contains(appearance.ColorId)) return false;
        return true;
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
            Persist();
        }
    }

    private void Persist()
    {
        lock (_gate)
        {
            _state.AllowedFormIds = _allowedFormIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
            File.WriteAllText(_path, JsonSerializer.Serialize(_state, Json));
        }
    }
}
