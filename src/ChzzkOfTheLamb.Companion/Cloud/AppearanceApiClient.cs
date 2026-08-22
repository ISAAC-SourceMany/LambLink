using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChzzkOfTheLamb.Protocol;

namespace ChzzkOfTheLamb.Companion.Cloud;

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
}
