using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ChzzkOfTheLamb.Protocol;

namespace ChzzkOfTheLamb.Companion.GameBridge;

public sealed class GameBridgeServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private WebSocket? _game;

    public event Action<GameCommandEnvelope>? MessageReceived;
    public event Action<bool>? ConnectionChanged;

    public bool IsGameConnected => _game?.State == WebSocketState.Open;

    public GameBridgeServer(string prefix = "http://127.0.0.1:17771/") => _listener.Prefixes.Add(prefix);

    public async Task RunAsync(CancellationToken ct)
    {
        _listener.Start();
        Console.WriteLine("[Bridge] waiting for game mod at ws://127.0.0.1:17771/game");
        while (!ct.IsCancellationRequested)
        {
            var ctx = await _listener.GetContextAsync().WaitAsync(ct);
            if (ctx.Request.Url?.AbsolutePath != "/game" || !ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                continue;
            }

            var accepted = await ctx.AcceptWebSocketAsync(null);
            _game?.Dispose();
            _game = accepted.WebSocket;
            Console.WriteLine("[Bridge] Cult of the Lamb mod connected.");
            ConnectionChanged?.Invoke(true);
            _ = ReceiveLoopAsync(_game, ct);
        }
    }

    public async Task<bool> SendAsync<T>(string type, T payload, CancellationToken ct)
    {
        if (_game?.State != WebSocketState.Open)
        {
            Console.WriteLine($"[Bridge] game not connected; skipped {type}");
            return false;
        }

        var locked = false;
        try
        {
            var envelope = new GameCommandEnvelope
            {
                Type = type,
                PayloadJson = JsonSerializer.Serialize(payload)
            };
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope));
            await _sendLock.WaitAsync(ct);
            locked = true;
            var game = _game;
            if (game?.State != WebSocketState.Open)
            {
                Console.WriteLine($"[Bridge] game disconnected before send; skipped {type}");
                return false;
            }
            await game.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"[Bridge][TX-FAILED] type={type}: {ex.GetBaseException().Message}");
            return false;
        }
        finally
        {
            if (locked) _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buffer, ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, r.Count);
                } while (!r.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                var envelope = JsonSerializer.Deserialize<GameCommandEnvelope>(json);
                if (envelope is not null) MessageReceived?.Invoke(envelope);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"[Bridge] game disconnected: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_game, ws))
            {
                _game = null;
                ConnectionChanged?.Invoke(false);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _game?.Dispose();
        _listener.Close();
        _sendLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
