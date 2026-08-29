using System.Net;
using System.Net.WebSockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LambLink.Protocol;

namespace LambLink.Companion.GameBridge;

public sealed class GameBridgeServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private WebSocket? _game;
    private long _sendSequence;
    private long _receiveSequence;

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
        var sequence = Interlocked.Increment(ref _sendSequence);
        var started = Stopwatch.GetTimestamp();
        if (_game?.State != WebSocketState.Open)
        {
            Console.WriteLine($"[Bridge][TX][SKIPPED] id={sequence}, type={type}: game not connected");
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
            Console.WriteLine($"[Bridge][TX][BEGIN] id={sequence}, type={type}, bytes={bytes.Length}, thread={Environment.CurrentManagedThreadId}");
            await _sendLock.WaitAsync(ct);
            locked = true;
            var game = _game;
            if (game?.State != WebSocketState.Open)
            {
                Console.WriteLine($"[Bridge][TX][ABORTED] id={sequence}, type={type}: game disconnected while waiting for send lock");
                return false;
            }
            await game.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            Console.WriteLine($"[Bridge][TX][END] id={sequence}, type={type}, elapsedMs={ElapsedMilliseconds(started):F1}");
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"[Bridge][TX][FAILED] id={sequence}, type={type}, elapsedMs={ElapsedMilliseconds(started):F1}: {ex.GetBaseException().Message}");
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

                var sequence = Interlocked.Increment(ref _receiveSequence);
                var bytes = ms.ToArray();
                Console.WriteLine($"[Bridge][RX][FRAME] id={sequence}, bytes={bytes.Length}, messageType={r.MessageType}, thread={Environment.CurrentManagedThreadId}");
                GameCommandEnvelope? envelope;
                try
                {
                    var json = Encoding.UTF8.GetString(bytes);
                    envelope = JsonSerializer.Deserialize<GameCommandEnvelope>(json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Bridge][RX][DECODE-FAILED] id={sequence}, bytes={bytes.Length}: {ex}");
                    continue;
                }
                if (envelope is not null)
                {
                    Console.WriteLine($"[Bridge][RX][DECODED] id={sequence}, type={envelope.Type}, payloadChars={envelope.PayloadJson.Length}");
                    MessageReceived?.Invoke(envelope);
                }
                else Console.WriteLine($"[Bridge][RX][EMPTY] id={sequence}, bytes={bytes.Length}");
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"[Bridge][RX][DISCONNECTED] socket={ws.State}, type={ex.GetType().FullName}, error={ex}");
        }
        finally
        {
            Console.WriteLine($"[Bridge][RX][LOOP-END] socket={ws.State}, cancellationRequested={ct.IsCancellationRequested}");
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

    private static double ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
}
