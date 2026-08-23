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
                    if (envelope is not null) queue.Enqueue(envelope);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning($"[BRIDGE][DISCONNECTED] {ex.GetBaseException().Message}; retrying in 3s");
                try { await System.Threading.Tasks.Task.Delay(3000, ct); } catch { }
            }
            finally { _socket = null; }
        }
    }

    public async System.Threading.Tasks.Task SendAsync<T>(string type, T payload, CancellationToken ct = default)
    {
        var ws = _socket;
        if (ws?.State != WebSocketState.Open) return;
        var envelope = new GameCommandEnvelope { Type = type, PayloadJson = JsonConvert.SerializeObject(payload) };
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
        await _sendLock.WaitAsync(ct);
        try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
        finally { _sendLock.Release(); }
    }
}
