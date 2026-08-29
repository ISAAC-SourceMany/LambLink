using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LambLink.Protocol;

namespace LambLink.Companion.Cloud;

public sealed class AppearanceApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private string? _companionToken;

    public string BaseUrl { get; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_companionToken);

    public AppearanceApiClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/") };
    }

    public async Task AuthenticateCompanionAsync(string chzzkAccessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "companion/session");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", chzzkAccessToken);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var payload = await res.Content.ReadFromJsonAsync<CompanionSessionResponse>(_json, ct)
                      ?? throw new InvalidOperationException("Cloud session response was empty.");
        _companionToken = payload.Token;
    }

    public async Task UploadCatalogAsync(string streamerChannelId, FollowerAppearanceCatalog catalog, IReadOnlyCollection<string> allowedFormIds, CancellationToken ct)
    {
        EnsureAuthenticated();
        var body = new
        {
            saveId = catalog.SaveId,
            generatedAt = catalog.GeneratedAt,
            forms = catalog.Forms,
            allowedFormIds = allowedFormIds.ToArray()
        };
        // Serialize first and send a StringContent with an explicit byte length. The local
        // Python BaseHTTPRequestHandler used during development does not automatically
        // decode HTTP/1.1 chunked request bodies. JsonContent may be sent chunked, which
        // previously made the server see an empty {} payload even though the Companion
        // had a valid 27-form catalog in memory.
        var jsonBody = JsonSerializer.Serialize(body, _json);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        content.Headers.ContentLength = Encoding.UTF8.GetByteCount(jsonBody);
        Console.WriteLine($"[CLOUD] catalog request payload: save={catalog.SaveId}, forms={catalog.Forms.Count}, allowed={allowedFormIds.Count}, bytes={content.Headers.ContentLength}");
        using var req = Authenticated(HttpMethod.Put, $"streamers/{Uri.EscapeDataString(streamerChannelId)}/catalog", content);
        using var res = await _http.SendAsync(req, ct);
        var responseText = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();

        CatalogUploadResponse? ack = null;
        try { ack = JsonSerializer.Deserialize<CatalogUploadResponse>(responseText, _json); }
        catch { }

        if (ack is null || !ack.Ok)
            throw new InvalidOperationException($"Catalog upload acknowledgement was invalid: {responseText}");
        if (!string.Equals(ack.SaveId, catalog.SaveId, StringComparison.Ordinal)
            || ack.Forms != catalog.Forms.Count
            || ack.Allowed != allowedFormIds.Count)
            throw new InvalidOperationException(
                $"Catalog upload mismatch. sent save={catalog.SaveId}, forms={catalog.Forms.Count}, allowed={allowedFormIds.Count}; " +
                $"server save={ack.SaveId}, forms={ack.Forms}, allowed={ack.Allowed}");
    }

    public async Task<FollowerAppearanceSelection?> GetViewerAppearanceAsync(string streamerChannelId, string viewerChannelId, CancellationToken ct)
    {
        if (!IsAuthenticated) return null;
        using var req = Authenticated(HttpMethod.Get,
            $"streamers/{Uri.EscapeDataString(streamerChannelId)}/viewers/{Uri.EscapeDataString(viewerChannelId)}/appearance", null);
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        var payload = await res.Content.ReadFromJsonAsync<ViewerAppearanceResponse>(_json, ct);
        return payload?.Appearance;
    }

    public async Task<FollowerAppearanceSelection?> FinalizeViewerAppearanceAsync(string streamerChannelId, string viewerChannelId, string viewerNickname, string saveId, int generation, string? raffleId, int recruitFollowerId, string followerName, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { saveId, generation, raffleId, recruitFollowerId, followerName, viewerNickname }, _json);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = Authenticated(HttpMethod.Post,
            $"streamers/{Uri.EscapeDataString(streamerChannelId)}/viewers/{Uri.EscapeDataString(viewerChannelId)}/appearance/finalize", content);
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        var payload = await res.Content.ReadFromJsonAsync<ViewerAppearanceResponse>(_json, ct);
        return payload?.Appearance;
    }

    public async Task UpdateReservationStatusAsync(string streamerChannelId, string viewerChannelId, string saveId, int generation, string? raffleId, string status, FollowerAppearanceSelection? appliedAppearance, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { saveId, generation, raffleId, status, appliedAppearance }, _json);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = Authenticated(HttpMethod.Post,
            $"streamers/{Uri.EscapeDataString(streamerChannelId)}/viewers/{Uri.EscapeDataString(viewerChannelId)}/appearance/status", content);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<PendingAppearanceReservation>> GetPendingReservationsAsync(string streamerChannelId, string saveId, CancellationToken ct)
    {
        using var req = Authenticated(HttpMethod.Get,
            $"streamers/{Uri.EscapeDataString(streamerChannelId)}/reservations?saveId={Uri.EscapeDataString(saveId)}", null);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var payload = await res.Content.ReadFromJsonAsync<ReservationListResponse>(_json, ct);
        return payload?.Reservations ?? new List<PendingAppearanceReservation>();
    }

    public async Task PutViewerStateAsync(string streamerChannelId, string viewerChannelId, object state, CancellationToken ct)
    {
        var jsonBody = JsonSerializer.Serialize(state, _json);
        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        content.Headers.ContentLength = Encoding.UTF8.GetByteCount(jsonBody);
        using var req = Authenticated(HttpMethod.Put,
            $"streamers/{Uri.EscapeDataString(streamerChannelId)}/viewers/{Uri.EscapeDataString(viewerChannelId)}/state", content);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<CloudViewerFollowerState>> GetFollowerStatesAsync(string streamerChannelId, string saveId, CancellationToken ct)
    {
        EnsureAuthenticated();
        var states = new List<CloudViewerFollowerState>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        do
        {
            var path = $"streamers/{Uri.EscapeDataString(streamerChannelId)}/follower-states?saveId={Uri.EscapeDataString(saveId)}";
            if (!string.IsNullOrWhiteSpace(cursor))
                path += $"&cursor={Uri.EscapeDataString(cursor)}";
            using var req = Authenticated(HttpMethod.Get, path, null);
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            var payload = await res.Content.ReadFromJsonAsync<FollowerStateListResponse>(_json, ct)
                          ?? throw new InvalidOperationException("Follower-state response was empty.");
            if (!string.Equals(payload.SaveId, saveId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Follower-state save mismatch. requested={saveId}, received={payload.SaveId}");
            states.AddRange(payload.States ?? new List<CloudViewerFollowerState>());
            cursor = payload.NextToken;
            if (!string.IsNullOrWhiteSpace(cursor) && !seenCursors.Add(cursor))
                throw new InvalidOperationException("Follower-state pagination returned a repeated cursor.");
        } while (!string.IsNullOrWhiteSpace(cursor));

        return states;
    }

    private HttpRequestMessage Authenticated(HttpMethod method, string path, HttpContent? content)
    {
        EnsureAuthenticated();
        var req = new HttpRequestMessage(method, path) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _companionToken);
        return req;
    }

    private void EnsureAuthenticated()
    {
        if (!IsAuthenticated) throw new InvalidOperationException("Cloud companion session has not been established.");
    }

    public void Dispose() => _http.Dispose();


    private sealed class CatalogUploadResponse
    {
        public bool Ok { get; set; }
        public string SaveId { get; set; } = string.Empty;
        public int Forms { get; set; }
        public int Allowed { get; set; }
    }

    private sealed class CompanionSessionResponse
    {
        public string Token { get; set; } = string.Empty;
    }

    private sealed class ViewerAppearanceResponse
    {
        public FollowerAppearanceSelection? Appearance { get; set; }
    }

    private sealed class ReservationListResponse
    {
        public List<PendingAppearanceReservation> Reservations { get; set; } = new();
    }

    private sealed class FollowerStateListResponse
    {
        public string SaveId { get; set; } = string.Empty;
        public List<CloudViewerFollowerState> States { get; set; } = new();
        public string? NextToken { get; set; }
    }
}

public sealed class PendingAppearanceReservation
{
    public string ViewerChannelId { get; set; } = string.Empty;
    public string ViewerNickname { get; set; } = string.Empty;
    public string FollowerName { get; set; } = string.Empty;
    public string? RaffleId { get; set; }
    public int RecruitFollowerId { get; set; }
    public int Generation { get; set; } = 1;
    public string Status { get; set; } = string.Empty;
    public long ReservedAt { get; set; }
    public long StatusUpdatedAt { get; set; }
    public FollowerAppearanceSelection? Appearance { get; set; }
    public FollowerAppearanceSelection? AppliedAppearance { get; set; }
}

public sealed class CloudViewerFollowerState
{
    public string SaveId { get; set; } = string.Empty;
    public string ViewerChannelId { get; set; } = string.Empty;
    public long Revision { get; set; }
    public bool CanCreate { get; set; }
    public int NextGeneration { get; set; } = 1;
    public List<CloudFollowerHistoryEntry> History { get; set; } = new();
}

public sealed class CloudFollowerHistoryEntry
{
    public int Generation { get; set; } = 1;
    public int FollowerId { get; set; }
    public string FollowerName { get; set; } = string.Empty;
    public string ViewerNickname { get; set; } = string.Empty;
    public bool IsAlive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DiedAt { get; set; }
    public string? DeathReason { get; set; }
    public DateTimeOffset? ResurrectedAt { get; set; }
    public FollowerAppearanceSelection? Appearance { get; set; }
    public List<CloudFollowerHistoryEvent> Events { get; set; } = new();
}

public sealed class CloudFollowerHistoryEvent
{
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public string? Cause { get; set; }
}
