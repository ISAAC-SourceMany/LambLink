using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace ChzzkOfTheLamb.Companion.Chzzk;

public static class LoopbackOAuth
{
    public static async Task<(string Code, string State)> AuthorizeAsync(
        ChzzkApiClient api, string redirectUri, CancellationToken ct)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var authUrl = api.BuildAuthorizationUrl(redirectUri, state);

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        var context = await listener.GetContextAsync().WaitAsync(ct);

        var code = context.Request.QueryString["code"];
        var returnedState = context.Request.QueryString["state"];
        var ok = code is not null && returnedState == state;
        var html = ok
            ? "<html><body><h2>CHZZK Companion 연결 완료</h2>이 창을 닫아도 됩니다.</body></html>"
            : "<html><body><h2>CHZZK Companion 연결 실패</h2>프로그램으로 돌아가 다시 시도하세요.</body></html>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, ct);
        context.Response.Close();

        if (!ok || code is null || returnedState is null)
            throw new InvalidOperationException("CHZZK OAuth callback validation failed.");

        return (code, returnedState);
    }
}
