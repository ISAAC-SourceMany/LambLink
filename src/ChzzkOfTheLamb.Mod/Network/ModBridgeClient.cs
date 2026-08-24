using System;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using BepInEx.Logging;
using ChzzkOfTheLamb.Protocol;

namespace ChzzkOfTheLamb.Mod.Network;

public sealed class ModBridgeClient(ConcurrentQueue<GameCommandEnvelope> queue, ManualLogSource log)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;

    public event Action<bool>? ConnectionChanged;

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
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, r.Count);
                    } while (!r.EndOfMessage);

                    if (r.MessageType == WebSocketMessageType.Close) break;
                    var envelope = JsonConvert.DeserializeObject<GameCommandEnvelope>(Encoding.UTF8.GetString(ms.ToArray()));
                    if (envelope is not null)
                    {
                        queue.Enqueue(envelope);
                        log.LogInfo($"[BRIDGE][RX-QUEUED] type={envelope.Type}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning($"[BRIDGE][DISCONNECTED] {ex.GetBaseException().Message}; retrying in 3s");
                try { await System.Threading.Tasks.Task.Delay(3000, ct); } catch { }
            }
            finally
            {
                _socket = null;
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
        var ws = _socket;
        if (ws?.State != WebSocketState.Open)
        {
            log.LogWarning($"[BRIDGE][SEND-SKIPPED] type={type}: socket is not open");
            return false;
        }

        var locked = false;
        try
        {
            var envelope = new GameCommandEnvelope { Type = type, PayloadJson = JsonConvert.SerializeObject(payload) };
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
            await _sendLock.WaitAsync(ct);
            locked = true;
            ws = _socket;
            if (ws?.State != WebSocketState.Open) return false;
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning($"[BRIDGE][SEND-FAILED] type={type}: {ex.GetBaseException().Message}");
            return false;
        }
        finally
        {
            if (locked) _sendLock.Release();
        }
    }

}
