using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ChzzkOfTheLamb.Companion.Chzzk;

/// <summary>
/// Minimal Engine.IO v3 / Socket.IO v2-compatible websocket transport for CHZZK Session API.
/// The outer loop is deliberately permanent: Companion may start before a broadcast, a live
/// can end/restart, or CHZZK may rotate/revoke a Session connection. In all of those cases the
/// client reacquires a fresh Session URL and subscribes CHAT/DONATION/SUBSCRIPTION again.
/// </summary>
public sealed class ChzzkRealtimeClient(ChzzkApiClient api)
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private static readonly string[] RequiredSubscriptions = { "CHAT", "DONATION", "SUBSCRIPTION" };

    public event Action<ChatEvent>? Chat;
    public event Action<DonationEvent>? Donation;
    public event Action<SubscriptionEvent>? Subscription;

    public async Task RunAsync(string accessToken, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            var connectedAtLeastOnce = false;
            try
            {
                attempt++;
                if (attempt == 1)
                    Console.WriteLine("[CHZZK] realtime supervisor started; Companion may be launched before or after the broadcast.");
                else
                    Console.WriteLine($"[CHZZK] acquiring a fresh realtime session (attempt {attempt})...");

                // This call is also our broadcast/session availability probe. If Companion starts
                // before a live is ready, it may fail; the supervisor simply keeps polling.
                var sessionUrl = await api.CreateUserSessionUrlAsync(accessToken, ct);
                connectedAtLeastOnce = await RunOneSessionAsync(sessionUrl, accessToken, ct);

                // A normal return means the socket closed without an exception. Treat that exactly
                // like a failure and reacquire a brand-new CHZZK Session URL + subscriptions.
                if (!ct.IsCancellationRequested)
                    Console.WriteLine("[CHZZK] realtime session ended; a fresh session will be created automatically.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CHZZK] realtime unavailable/disconnected: {ex.GetType().Name}: {ex.Message}");
            }

            if (ct.IsCancellationRequested) break;

            // Reset backoff after a session was actually established. Before a live/session is
            // available, gently back off so Companion can sit open indefinitely without hammering API.
            if (connectedAtLeastOnce) attempt = 0;
            // After a genuinely established session fails, reconnect quickly to minimize the
            // unavoidable gap. Before first availability, keep the gentler backoff.
            var delaySeconds = connectedAtLeastOnce ? 1 : Math.Min(15, 2 + attempt * 2);
            Console.WriteLine($"[CHZZK] reconnect scheduled in {delaySeconds}s (broadcast can be started/stopped in any order).");
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    private async Task<bool> RunOneSessionAsync(string sessionUrl, string accessToken, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        // Keep the underlying websocket alive without treating normal CHZZK chat silence as
        // a broken Engine.IO session. dev10f used an application-level receive timeout based on
        // pingInterval+pingTimeout, but CHZZK can remain legitimately silent longer than that.
        // Forcing a reconnect in that case created a several-second blind spot where CHAT events
        // were not replayed. WebSocket-level keepalive + actual socket/SYSTEM errors are safer.
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        var websocketUri = BuildSocketIoWebSocketUri(sessionUrl);
        Console.WriteLine($"[CHZZK] realtime connecting: {websocketUri.Scheme}://{websocketUri.Host}:{websocketUri.Port}{websocketUri.AbsolutePath}");
        await ws.ConnectAsync(websocketUri, ct);

        var buffer = new byte[64 * 1024];
        var opened = false;
        var systemConnected = false;
        DateTimeOffset? subscriptionsRequestedAt = null;
        var subscribed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long sessionPacketCount = 0;
        long sessionChatCount = 0;
        long sessionDonationCount = 0;

        // CHZZK explicitly uses Engine.IO v3 (Socket.IO v1/v2). In EIO=3 the heartbeat direction
        // is CLIENT -> ping(2) -> SERVER -> pong(3). Engine.IO v4 reversed this direction.
        // Previous builds waited for a server ping, so the CHZZK server eventually stopped
        // delivering events even though the websocket still looked OPEN.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? heartbeatTask = null;
        Exception? heartbeatFailure = null;
        var sendGate = new SemaphoreSlim(1, 1);
        var heartbeat = new EngineIoHeartbeatState();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            string? text;
            try
            {
                text = await ReceiveTextAsync(ws, buffer, heartbeatCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && heartbeatFailure is not null)
            {
                throw new InvalidOperationException("Engine.IO v3 heartbeat failed", heartbeatFailure);
            }
            if (text is not null) sessionPacketCount++;

            if (text is null)
            {
                heartbeatCts.Cancel();
                if (heartbeatTask is not null)
                {
                    try { await heartbeatTask; } catch { }
                }
                return systemConnected;
            }

            // If subscribe REST calls were accepted but CHZZK never confirms all subscriptions,
            // recycle the session rather than remaining 'connected' but silently missing chat.
            if (subscriptionsRequestedAt.HasValue && subscribed.Count < RequiredSubscriptions.Length &&
                DateTimeOffset.UtcNow - subscriptionsRequestedAt.Value > TimeSpan.FromSeconds(20))
            {
                throw new InvalidOperationException(
                    "subscription confirmation timeout; confirmed=" +
                    (subscribed.Count == 0 ? "none" : string.Join(",", subscribed.OrderBy(x => x))));
            }

            // EIO=3 normally sends pong(3) in response to our client ping(2). Some compatible
            // servers may still send a ping, so answer it defensively as well.
            if (text == "2")
            {
                await SendTextSerializedAsync(ws, sendGate, "3", ct);
                Console.WriteLine("[CHZZK] Engine.IO server ping received -> pong sent.");
                continue;
            }
            if (text == "3")
            {
                heartbeat.MarkPong();
                continue;
            }

            // Engine.IO open. For EIO=3 the client MUST actively send ping packets at pingInterval.
            if (text.StartsWith("0", StringComparison.Ordinal))
            {
                opened = true;
                var hb = ParseHeartbeat(text);
                var interval = hb?.PingInterval ?? TimeSpan.FromSeconds(25);
                var timeout = hb?.PingTimeout ?? TimeSpan.FromSeconds(60);
                Console.WriteLine($"[CHZZK] Engine.IO v3 handshake opened; client heartbeat ping every {interval.TotalSeconds:0.#}s, pong timeout {timeout.TotalSeconds:0.#}s.");

                heartbeatTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!heartbeatCts.IsCancellationRequested && ws.State == WebSocketState.Open)
                        {
                            await Task.Delay(interval, heartbeatCts.Token);
                            heartbeat.MarkPing();
                            await SendTextSerializedAsync(ws, sendGate, "2", heartbeatCts.Token);
                            Console.WriteLine("[CHZZK] Engine.IO v3 ping sent.");

                            var deadline = DateTimeOffset.UtcNow + timeout;
                            while (!heartbeatCts.IsCancellationRequested && heartbeat.IsAwaitingPong)
                            {
                                if (DateTimeOffset.UtcNow >= deadline)
                                    throw new TimeoutException($"no Engine.IO pong within {timeout.TotalSeconds:0.#}s");
                                await Task.Delay(250, heartbeatCts.Token);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (heartbeatCts.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        heartbeatFailure = ex;
                        try { heartbeatCts.Cancel(); } catch { }
                    }
                }, heartbeatCts.Token);
                continue;
            }

            if (text == "40")
            {
                Console.WriteLine("[CHZZK] Socket.IO root namespace connected.");
                continue;
            }
            if (text == "41")
                throw new InvalidOperationException("Socket.IO root namespace disconnected by server");

            // Some Socket.IO servers report namespace/auth failures as event packets instead of 41.
            if (!text.StartsWith("42", StringComparison.Ordinal))
                continue;

            using var doc = JsonDocument.Parse(text.Substring(2));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2) continue;

            var eventName = root[0].GetString();
            var payload = root[1].ValueKind == JsonValueKind.String ? root[1].GetString()! : root[1].GetRawText();

            if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Socket.IO error: " + Abbreviate(payload, 500));

            if (eventName == "SYSTEM")
            {
                using var system = JsonDocument.Parse(payload);
                var type = system.RootElement.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
                if (type == "connected")
                {
                    systemConnected = true;
                    var sessionKey = system.RootElement.GetProperty("data").GetProperty("sessionKey").GetString()!;
                    Console.WriteLine($"[CHZZK] session connected: key={Abbreviate(sessionKey, 8)}");
                    await api.SubscribeAsync(accessToken, sessionKey, "chat", ct);
                    await api.SubscribeAsync(accessToken, sessionKey, "donation", ct);
                    await api.SubscribeAsync(accessToken, sessionKey, "subscription", ct);
                    subscriptionsRequestedAt = DateTimeOffset.UtcNow;
                    subscribed.Clear();
                    Console.WriteLine("[CHZZK] subscribe requests accepted: CHAT / DONATION / SUBSCRIPTION; waiting for SYSTEM confirmations...");
                }
                else if (type is "subscribed" or "unsubscribed" or "revoked")
                {
                    var data = system.RootElement.TryGetProperty("data", out var dataNode) ? dataNode : default;
                    var eventType = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("eventType", out var eventTypeNode)
                        ? eventTypeNode.GetString()
                        : "?";
                    var channelId = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("channelId", out var channelNode)
                        ? channelNode.GetString()
                        : "?";
                    Console.WriteLine($"[CHZZK] SYSTEM {type}: event={eventType}, channel={channelId}");

                    if (type == "subscribed" && !string.IsNullOrWhiteSpace(eventType) && eventType != "?")
                    {
                        subscribed.Add(eventType.ToUpperInvariant());
                        if (RequiredSubscriptions.All(x => subscribed.Contains(x)))
                        {
                            subscriptionsRequestedAt = null;
                            Console.WriteLine("[CHZZK] realtime READY: CHAT / DONATION / SUBSCRIPTION confirmed.");
                        }
                    }
                    else
                    {
                        // Broadcast end/session rotation can revoke or unsubscribe events while the
                        // websocket itself still looks open. Force a full session refresh immediately.
                        throw new InvalidOperationException($"CHZZK {type} {eventType}; refreshing realtime session");
                    }
                }
                else
                {
                    Console.WriteLine($"[CHZZK] SYSTEM {type ?? "?"}: {Abbreviate(payload, 500)}");
                }
                continue;
            }

            try
            {
                switch (eventName)
                {
                    case "CHAT":
                    {
                        sessionChatCount++;
                        Console.WriteLine($"[CHZZK] CHAT frame received: sessionChat={sessionChatCount}, packets={sessionPacketCount}, bytes={Encoding.UTF8.GetByteCount(payload)}");
                        var chat = DeserializeEventPayload<ChatEvent>(payload);
                        if (chat is null)
                        {
                            Console.Error.WriteLine($"[CHZZK] CHAT parse failed: payload={Abbreviate(payload, 1200)}");
                            break;
                        }
                        Chat?.Invoke(chat);
                        break;
                    }
                    case "DONATION":
                    {
                        sessionDonationCount++;
                        Console.WriteLine($"[CHZZK] DONATION frame received: sessionDonation={sessionDonationCount}, packets={sessionPacketCount}, bytes={Encoding.UTF8.GetByteCount(payload)}");
                        var donation = DeserializeEventPayload<DonationEvent>(payload);
                        if (donation is not null) Donation?.Invoke(donation);
                        else Console.Error.WriteLine($"[CHZZK] DONATION parse failed: payload={Abbreviate(payload, 1200)}");
                        break;
                    }
                    case "SUBSCRIPTION":
                    {
                        var subscription = DeserializeEventPayload<SubscriptionEvent>(payload);
                        if (subscription is not null) Subscription?.Invoke(subscription);
                        else Console.Error.WriteLine($"[CHZZK] SUBSCRIPTION parse failed: payload={Abbreviate(payload, 1200)}");
                        break;
                    }
                    default:
                        Console.WriteLine($"[CHZZK] unhandled socket event={eventName ?? "?"}, payload={Abbreviate(payload, 500)}");
                        break;
                }
            }
            catch (Exception ex)
            {
                // A malformed user event must not tear down the whole realtime connection.
                Console.Error.WriteLine($"[CHZZK] {eventName ?? "?"} event handler error: {ex.GetType().Name}: {ex.Message}; payload={Abbreviate(payload, 1200)}");
            }
        }

        return opened || systemConnected;
    }

    private sealed record HeartbeatSettings(TimeSpan PingInterval, TimeSpan PingTimeout);

    private static HeartbeatSettings? ParseHeartbeat(string engineIoOpenPacket)
    {
        try
        {
            using var doc = JsonDocument.Parse(engineIoOpenPacket.Substring(1));
            var root = doc.RootElement;
            var pingInterval = root.TryGetProperty("pingInterval", out var i) && i.TryGetInt32(out var iv) ? iv : 25000;
            var pingTimeout = root.TryGetProperty("pingTimeout", out var t) && t.TryGetInt32(out var tv) ? tv : 60000;
            return new HeartbeatSettings(
                TimeSpan.FromMilliseconds(Math.Clamp(pingInterval, 1000, 120000)),
                TimeSpan.FromMilliseconds(Math.Clamp(pingTimeout, 1000, 180000)));
        }
        catch { return null; }
    }

    private sealed class EngineIoHeartbeatState
    {
        private long _pingGeneration;
        private long _pongGeneration;
        public bool IsAwaitingPong => Interlocked.Read(ref _pongGeneration) < Interlocked.Read(ref _pingGeneration);
        public void MarkPing() => Interlocked.Increment(ref _pingGeneration);
        public void MarkPong() => Interlocked.Exchange(ref _pongGeneration, Interlocked.Read(ref _pingGeneration));
    }

    private T? DeserializeEventPayload<T>(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var node = doc.RootElement;
            if (node.ValueKind == JsonValueKind.Object &&
                node.TryGetProperty("data", out var dataNode) &&
                dataNode.ValueKind == JsonValueKind.Object)
            {
                node = dataNode;
            }
            return JsonSerializer.Deserialize<T>(node.GetRawText(), _json);
        }
        catch { return default; }
    }

    private static string Abbreviate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max] + "...";
    }

    private static Uri BuildSocketIoWebSocketUri(string sessionUrl)
    {
        var source = new Uri(sessionUrl, UriKind.Absolute);
        var builder = new UriBuilder(source);
        builder.Scheme = source.Scheme.ToLowerInvariant() switch
        {
            "https" => "wss",
            "http" => "ws",
            "wss" => "wss",
            "ws" => "ws",
            _ => throw new InvalidOperationException($"Unsupported CHZZK session URL scheme: {source.Scheme}")
        };
        if (string.IsNullOrEmpty(source.AbsolutePath) || source.AbsolutePath == "/")
            builder.Path = "/socket.io/";
        var query = source.Query.TrimStart('?');
        if (!ContainsQueryKey(query, "EIO")) query = AppendQuery(query, "EIO=3");
        if (!ContainsQueryKey(query, "transport")) query = AppendQuery(query, "transport=websocket");
        builder.Query = query;
        return builder.Uri;
    }

    private static bool ContainsQueryKey(string query, string key)
    {
        if (string.IsNullOrEmpty(query)) return false;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var candidate = eq >= 0 ? part[..eq] : part;
            if (Uri.UnescapeDataString(candidate).Equals(key, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string AppendQuery(string query, string pair)
        => string.IsNullOrEmpty(query) ? pair : query + "&" + pair;

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) continue;
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task SendTextSerializedAsync(ClientWebSocket ws, SemaphoreSlim gate, string text, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private static Task SendTextAsync(ClientWebSocket ws, string text, CancellationToken ct)
        => ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
}
