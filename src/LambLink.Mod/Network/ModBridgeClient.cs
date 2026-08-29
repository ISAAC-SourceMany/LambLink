using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using BepInEx.Logging;
using LambLink.Protocol;

namespace LambLink.Mod.Network;

public sealed class ModBridgeClient(ConcurrentQueue<GameCommandEnvelope> queue, ManualLogSource log)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private long _receiveSequence;
    private long _sendSequence;

    public event Action<bool>? ConnectionChanged;
    public event Action<long, GameCommandEnvelope>? CommandDecoded;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async System.Threading.Tasks.Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                _socket = ws;
                log.LogInfo("[BRIDGE][CONNECT] attempting ws://127.0.0.1:17771/game");
                await ws.ConnectAsync(new Uri("ws://127.0.0.1:17771/game"), ct);
                log.LogInfo("[BRIDGE][CONNECTED] Connected to CHZZK Companion at ws://127.0.0.1:17771/game");
                ConnectionChanged?.Invoke(true);

                var buffer = new byte[32 * 1024];
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    log.LogInfo($"[BRIDGE][RX][WAIT] socket={ws.State}, thread={Thread.CurrentThread.ManagedThreadId}");
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, r.Count);
                    } while (!r.EndOfMessage);

                    if (r.MessageType == WebSocketMessageType.Close) break;
                    var sequence = Interlocked.Increment(ref _receiveSequence);
                    var bytes = ms.ToArray();
                    log.LogInfo($"[BRIDGE][RX][FRAME] id={sequence}, bytes={bytes.Length}, messageType={r.MessageType}, thread={Thread.CurrentThread.ManagedThreadId}");
                    GameCommandEnvelope? envelope;
                    try
                    {
                        envelope = JsonConvert.DeserializeObject<GameCommandEnvelope>(Encoding.UTF8.GetString(bytes));
                    }
                    catch (Exception ex)
                    {
                        log.LogError($"[BRIDGE][RX][DECODE-FAILED] id={sequence}, bytes={bytes.Length}: {ex}");
                        continue;
                    }
                    if (envelope is not null)
                    {
                        envelope.DiagnosticSequence = sequence;
                        envelope.DiagnosticReceivedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        queue.Enqueue(envelope);
                        log.LogInfo($"[BRIDGE][RX][QUEUED] id={sequence}, type={envelope.Type}, queue={queue.Count}");
                        try { CommandDecoded?.Invoke(sequence, envelope); }
                        catch (Exception ex) { log.LogError($"[BRIDGE][RX][CALLBACK-FAILED] id={sequence}, type={envelope.Type}: {ex}"); }
                    }
                    else log.LogWarning($"[BRIDGE][RX][EMPTY] id={sequence}, bytes={bytes.Length}");
                }
                log.LogWarning($"[BRIDGE][RX][LOOP-END] socket={ws.State}, cancellationRequested={ct.IsCancellationRequested}");
            }
            catch (Exception ex)
            {
                log.LogWarning($"[BRIDGE][DISCONNECTED] type={ex.GetType().FullName}, cancellationRequested={ct.IsCancellationRequested}, error={ex}; retrying in 3s");
                try { await System.Threading.Tasks.Task.Delay(3000, ct); } catch { }
            }
            finally
            {
                _socket = null;
                log.LogInfo($"[BRIDGE][CONNECTION-CLEANUP] cancellationRequested={ct.IsCancellationRequested}");
                ConnectionChanged?.Invoke(false);
            }
        }
    }

    public async System.Threading.Tasks.Task SendAsync<T>(string type, T payload, CancellationToken ct = default)
    {
        await TrySendAsync(type, payload, ct);
    }
    public async System.Threading.Tasks.Task<bool> TrySendAsync<T>(string type, T payload, CancellationToken ct = default)
    {
        var sequence = Interlocked.Increment(ref _sendSequence);
        var started = Stopwatch.GetTimestamp();
        var ws = _socket;
        if (ws?.State != WebSocketState.Open)
        {
            log.LogWarning($"[BRIDGE][TX][SKIPPED] id={sequence}, type={type}: socket is not open");
            return false;
        }

        var locked = false;
        try
        {
            var envelope = new GameCommandEnvelope { Type = type, PayloadJson = JsonConvert.SerializeObject(payload) };
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            log.LogInfo($"[BRIDGE][TX][BEGIN] id={sequence}, type={type}, bytes={bytes.Length}, thread={Thread.CurrentThread.ManagedThreadId}");
            await _sendLock.WaitAsync(ct);
            locked = true;
            ws = _socket;
            if (ws?.State != WebSocketState.Open)
            {
                log.LogWarning($"[BRIDGE][TX][ABORTED] id={sequence}, type={type}: socket closed while waiting for send lock");
                return false;
            }
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            log.LogInfo($"[BRIDGE][TX][END] id={sequence}, type={type}, elapsedMs={ElapsedMilliseconds(started):F1}");
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning($"[BRIDGE][TX][FAILED] id={sequence}, type={type}, elapsedMs={ElapsedMilliseconds(started):F1}: {ex.GetBaseException().Message}");
            return false;
        }
        finally
        {
            if (locked) _sendLock.Release();
        }
    }

    private static double ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

}
