using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChzzkOfTheLamb.Companion.Chzzk;

public sealed record ProductionOAuthResult(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    long ExpiresIn,
    string StreamerChannelId,
    string StreamerChannelName);

public static class ProductionOAuth
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<ProductionOAuthResult> AuthorizeAsync(
        HttpClient http,
        string apiBaseUrl,
        string loopbackRedirectUri,
        CancellationToken ct)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var startUrl = apiBaseUrl.TrimEnd('/') + "/auth/companion/start" +
                       $"?callback={Uri.EscapeDataString(loopbackRedirectUri)}" +
                       $"&nonce={Uri.EscapeDataString(nonce)}";

        using var listener = new HttpListener();
        listener.Prefixes.Add(loopbackRedirectUri);
        listener.Start();

        Console.WriteLine("[AUTH] 브라우저에서 CHZZK 연결을 승인해주세요.");
        Process.Start(new ProcessStartInfo(startUrl) { UseShellExecute = true });

        var context = await listener.GetContextAsync().WaitAsync(ct);
        var returnedNonce = context.Request.QueryString["nonce"];
        var ticket = context.Request.QueryString["ticket"];
        var error = context.Request.QueryString["error"];
        var ok = error is null && ticket is not null && returnedNonce == nonce;

        var html = ok
            ? "<html><meta charset='utf-8'><body><h2>CHZZK Companion 연결 완료</h2>이 창을 닫고 프로그램으로 돌아가세요.</body></html>"
            : "<html><meta charset='utf-8'><body><h2>CHZZK Companion 연결 실패</h2>프로그램으로 돌아가 다시 시도하세요.</body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, ct);
        context.Response.Close();

        if (!ok || ticket is null)
            throw new InvalidOperationException(error is null
                ? "Production OAuth callback validation failed."
                : $"Production OAuth failed: {error}");

        using var response = await http.PostAsJsonAsync(
            apiBaseUrl.TrimEnd('/') + "/auth/companion/token",
            new { ticket, nonce }, Json, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        var payload = JsonSerializer.Deserialize<TokenResponse>(responseText, Json)
                      ?? throw new InvalidOperationException("Auth gateway returned an empty token response.");

        if (string.IsNullOrWhiteSpace(payload.AccessToken) || string.IsNullOrWhiteSpace(payload.StreamerChannelId))
            throw new InvalidOperationException("Auth gateway returned an invalid token response.");

        Console.WriteLine($"[AUTH] CHZZK connected: {payload.StreamerChannelName} ({payload.StreamerChannelId})");
        return new ProductionOAuthResult(
            payload.AccessToken,
            payload.RefreshToken ?? string.Empty,
            payload.TokenType ?? "Bearer",
            payload.ExpiresIn,
            payload.StreamerChannelId,
            payload.StreamerChannelName ?? string.Empty);
    }

    private sealed class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public string? TokenType { get; set; }
        public long ExpiresIn { get; set; }
        public string StreamerChannelId { get; set; } = string.Empty;
        public string? StreamerChannelName { get; set; }
    }
}
