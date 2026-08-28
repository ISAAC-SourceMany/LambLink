using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ChzzkOfTheLamb.Companion.Chzzk;

public sealed class ChzzkApiClient
{
    private readonly HttpClient http;
    private readonly string? clientId;
    private readonly string? clientSecret;

    public ChzzkApiClient(HttpClient http, string? clientId = null, string? clientSecret = null)
    {
        this.http = http;
        this.clientId = clientId;
        this.clientSecret = clientSecret;
    }
    private const string BaseUrl = "https://openapi.chzzk.naver.com";
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        EnsureClientCredentials();
        return "https://chzzk.naver.com/account-interlock" +
               $"?clientId={Uri.EscapeDataString(clientId!)}" +
               $"&redirectUri={Uri.EscapeDataString(redirectUri)}" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    public async Task<ChzzkTokenSet> ExchangeCodeAsync(string code, string state, CancellationToken ct)
    {
        EnsureClientCredentials();
        var body = new
        {
            grantType = "authorization_code",
            clientId = clientId!,
            clientSecret = clientSecret!,
            code,
            state
        };

        var response = await PostJsonAsync<JsonElement>("/auth/v1/token", body, bearer: null, ct);
        var c = response.Content;
        if (c.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidOperationException(response.Message ?? "No token content returned.");

        return new ChzzkTokenSet(
            c.GetProperty("accessToken").GetString()!,
            c.GetProperty("refreshToken").GetString()!,
            c.GetProperty("tokenType").GetString() ?? "Bearer",
            ParseExpiresIn(c.GetProperty("expiresIn")),
            DateTimeOffset.UtcNow);
    }

    public async Task<ChzzkTokenSet> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        EnsureClientCredentials();
        var response = await PostJsonAsync<JsonElement>("/auth/v1/token", new
        {
            grantType = "refresh_token",
            refreshToken,
            clientId = clientId!,
            clientSecret = clientSecret!
        }, bearer: null, ct);
        var c = response.Content;
        if (c.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidOperationException(response.Message ?? "No refreshed token content returned.");
        return new ChzzkTokenSet(
            c.GetProperty("accessToken").GetString()!, c.GetProperty("refreshToken").GetString()!,
            c.GetProperty("tokenType").GetString() ?? "Bearer", ParseExpiresIn(c.GetProperty("expiresIn")), DateTimeOffset.UtcNow);
    }

    public async Task<MeContent> GetMeAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/open/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var envelope = await JsonSerializer.DeserializeAsync<ChzzkApiEnvelope<MeContent>>(
            await response.Content.ReadAsStreamAsync(ct), _json, ct);
        return envelope?.Content ?? throw new InvalidOperationException(envelope?.Message ?? "Unable to read CHZZK user.");
    }

    public async Task<string> CreateUserSessionUrlAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/open/v1/sessions/auth");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var envelope = await JsonSerializer.DeserializeAsync<ChzzkApiEnvelope<SessionUrlContent>>(
            await response.Content.ReadAsStreamAsync(ct), _json, ct);
        return envelope?.Content?.Url ?? throw new InvalidOperationException(envelope?.Message ?? "No session URL returned.");
    }

    public Task SubscribeAsync(string accessToken, string sessionKey, string eventName, CancellationToken ct)
        => PostEmptyAuthorizedAsync($"/open/v1/sessions/events/subscribe/{eventName}?sessionKey={Uri.EscapeDataString(sessionKey)}", accessToken, ct);

    private async Task PostEmptyAuthorizedAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<ChzzkApiEnvelope<T>> PostJsonAsync<T>(string path, object body, string? bearer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await JsonSerializer.DeserializeAsync<ChzzkApiEnvelope<T>>(
            await response.Content.ReadAsStreamAsync(ct), _json, ct))
               ?? throw new InvalidOperationException("Empty CHZZK response.");
    }

    private void EnsureClientCredentials()
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("This CHZZK API operation requires client credentials and is development-only in the distributed Companion.");
    }

    private static long ParseExpiresIn(JsonElement e)
        => e.ValueKind == JsonValueKind.Number ? e.GetInt64() : long.Parse(e.GetString() ?? "86400");
}
