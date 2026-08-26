using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ChzzkOfTheLamb.Companion.Overlay;

public sealed class RaffleOverlayServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    private string _phase = "hidden";
    private string _command = "!신도";
    private DateTimeOffset? _endsAt;
    private DateTimeOffset? _visibleUntil;
    private int _participantCount;
    private string? _winnerNickname;
    private string? _donationNickname;
    private long _donationAmount;
    private string? _donationEventName;
    private readonly LinkedList<DonationOverlayItem> _donationQueue = new();
    private DonationOverlayItem? _activeDonation;
    private long _donationSequence;
    private long _overlayPageRequests;
    private long _stateRequests;
    private DateTimeOffset? _lastStateRequestAt;
    private readonly Dictionary<string, List<ScheduledOverlayBuff>> _buffQueues = new(StringComparer.Ordinal);
    private readonly List<string> _buffOrder = new();
    private long _buffGroupSequence;
    private bool _buffTimersPaused = true;
    private DateTimeOffset? _buffPauseStartedAt = DateTimeOffset.UtcNow;
    private string _buffPauseReason = "STARTING";

    public RaffleOverlayServer(int port = 17883)
    {
        _port = port;
    }

    public string OverlayUrl => $"http://127.0.0.1:{_port}/overlay";

    public bool IsClientPolling
    {
        get
        {
            lock (_gate)
                return _lastStateRequestAt.HasValue && DateTimeOffset.UtcNow - _lastStateRequestAt.Value < TimeSpan.FromSeconds(3);
        }
    }

    public double StatePollAgeSeconds
    {
        get
        {
            lock (_gate)
                return _lastStateRequestAt.HasValue
                    ? Math.Max(0, (DateTimeOffset.UtcNow - _lastStateRequestAt.Value).TotalSeconds)
                    : -1;
        }
    }

    public void Start()
    {
        if (_listener is not null) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        Console.WriteLine($"[OVERLAY] OBS browser source: {OverlayUrl}");
        Console.WriteLine("[OVERLAY][LAYOUT] donation=viewport-top-left-18,width=480px-only,buffs=viewport-left-to-right");
    }

    public void Open(int durationSeconds, string command)
    {
        bool clientPolling;
        long pageRequests;
        long stateRequests;
        string activeCommand;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            AdvanceTransientPhaseLocked(now);
            RequeueActiveDonationLocked(now, "raffle-opened");
            _phase = "raffle";
            _command = string.IsNullOrWhiteSpace(command) ? "!신도" : command;
            _endsAt = now.AddSeconds(Math.Max(1, durationSeconds));
            _visibleUntil = null;
            _participantCount = 0;
            _winnerNickname = null;
            clientPolling = _lastStateRequestAt.HasValue && DateTimeOffset.UtcNow - _lastStateRequestAt.Value < TimeSpan.FromSeconds(3);
            pageRequests = _overlayPageRequests;
            stateRequests = _stateRequests;
            activeCommand = _command;
        }
        Console.WriteLine($"[OVERLAY][RAFFLE-OPEN] duration={Math.Max(1, durationSeconds)}s, command={activeCommand}, clientPolling={clientPolling}, pageRequests={pageRequests}, statePolls={stateRequests}");
    }

    public void SetParticipantCount(int count)
    {
        lock (_gate) _participantCount = Math.Max(0, count);
    }

    public void ShowWinner(string nickname, int seconds = 5)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            AdvanceTransientPhaseLocked(now);
            RequeueActiveDonationLocked(now, "raffle-winner");
            _phase = "winner";
            _endsAt = null;
            _visibleUntil = now.AddSeconds(Math.Max(1, seconds));
            _winnerNickname = nickname;
        }
    }

    public void ShowNoParticipants(int seconds = 3)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            AdvanceTransientPhaseLocked(now);
            RequeueActiveDonationLocked(now, "raffle-empty");
            _phase = "empty";
            _endsAt = null;
            _visibleUntil = now.AddSeconds(Math.Max(1, seconds));
            _winnerNickname = null;
        }
    }

    public void ShowCancelled(int seconds = 2)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            AdvanceTransientPhaseLocked(now);
            RequeueActiveDonationLocked(now, "raffle-cancelled");
            _phase = "cancelled";
            _endsAt = null;
            _visibleUntil = now.AddSeconds(Math.Max(1, seconds));
            _winnerNickname = null;
        }
    }


    public void ShowDonation(string nickname, long amount, string eventName, int seconds = 5)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            AdvanceTransientPhaseLocked(now);
            var item = new DonationOverlayItem(
                ++_donationSequence,
                string.IsNullOrWhiteSpace(nickname) ? "후원자" : nickname,
                Math.Max(0, amount),
                string.IsNullOrWhiteSpace(eventName) ? "이벤트 발동" : eventName,
                TimeSpan.FromSeconds(Math.Max(1, seconds)));

            _donationQueue.AddLast(item);
            Console.WriteLine($"[OVERLAY][DONATION-QUEUE][ENQUEUED] sequence={item.Sequence}, phase={_phase}, pending={_donationQueue.Count}, event='{item.EventName}'");
            StartNextDonationLocked(now, "queue-ready");
        }
    }

    private void AdvanceTransientPhaseLocked(DateTimeOffset now)
    {
        if (_phase == "raffle" || !_visibleUntil.HasValue || now < _visibleUntil.Value) return;

        if (_phase == "donation" && _activeDonation is not null)
            Console.WriteLine($"[OVERLAY][DONATION-QUEUE][COMPLETED] sequence={_activeDonation.Sequence}, pending={_donationQueue.Count}, event='{_activeDonation.EventName}'");

        _phase = "hidden";
        _visibleUntil = null;
        _winnerNickname = null;
        _donationNickname = null;
        _donationAmount = 0;
        _donationEventName = null;
        _activeDonation = null;
        StartNextDonationLocked(now, "previous-card-completed");
    }

    private void StartNextDonationLocked(DateTimeOffset now, string reason)
    {
        if (_phase != "hidden" || _donationQueue.First is null) return;
        var item = _donationQueue.First.Value;
        _donationQueue.RemoveFirst();
        ActivateDonationLocked(item, now, reason);
    }

    private void ActivateDonationLocked(DonationOverlayItem item, DateTimeOffset now, string reason)
    {
        _activeDonation = item;
        _phase = "donation";
        _endsAt = null;
        _visibleUntil = now + item.DisplayDuration;
        _donationNickname = item.Nickname;
        _donationAmount = item.Amount;
        _donationEventName = item.EventName;
        Console.WriteLine($"[OVERLAY][DONATION-QUEUE][DISPLAY] sequence={item.Sequence}, reason={reason}, durationMs={item.DisplayDuration.TotalMilliseconds:F0}, pending={_donationQueue.Count}, event='{item.EventName}'");
    }

    private void RequeueActiveDonationLocked(DateTimeOffset now, string reason)
    {
        var active = _activeDonation;
        if (_phase != "donation" || active is null) return;

        var remaining = _visibleUntil.HasValue ? _visibleUntil.Value - now : active.DisplayDuration;
        if (remaining < TimeSpan.FromSeconds(1)) remaining = TimeSpan.FromSeconds(1);
        var resumed = active with { DisplayDuration = remaining };
        _donationQueue.AddFirst(resumed);
        Console.WriteLine($"[OVERLAY][DONATION-QUEUE][PREEMPTED] sequence={resumed.Sequence}, reason={reason}, remainingMs={remaining.TotalMilliseconds:F0}, pending={_donationQueue.Count}");
        _activeDonation = null;
        _donationNickname = null;
        _donationAmount = 0;
        _donationEventName = null;
    }

    public void RegisterDonationBuff(string effect, string eventName)
    {
        var definitions = GetBuffDefinitions(effect, eventName);
        if (definitions.Count == 0) return;

        lock (_gate)
        {
            var now = GetBuffNowLocked();
            var groupStartsAt = now;

            // First reserve every lane involved in this donation and find the latest
            // time at which all of them are free. This prevents a composite donation
            // from starting its speed card now while its attack card waits in queue.
            foreach (var definition in definitions)
            {
                if (!_buffQueues.TryGetValue(definition.Key, out var queue))
                {
                    queue = new List<ScheduledOverlayBuff>();
                    _buffQueues[definition.Key] = queue;
                    _buffOrder.Add(definition.Key);
                }

                queue.RemoveAll(x => x.ExpiresAt <= now);
                if (queue.Count == 0)
                {
                    _buffOrder.Remove(definition.Key);
                    _buffOrder.Add(definition.Key);
                }

                if (queue.Count > 0 && queue[^1].ExpiresAt > groupStartsAt)
                    groupStartsAt = queue[^1].ExpiresAt;
            }

            var group = ++_buffGroupSequence;
            var groupDelay = Math.Max(0, (int)Math.Ceiling((groupStartsAt - now).TotalSeconds));
            Console.WriteLine($"[OVERLAY][BUFF-GROUP] group={group}, effect={effect}, members={definitions.Count}, startsIn={groupDelay}s");

            foreach (var definition in definitions)
            {
                var queue = _buffQueues[definition.Key];
                var expiresAt = groupStartsAt.AddSeconds(definition.DurationSeconds);
                queue.Add(new ScheduledOverlayBuff(
                    definition.Key, definition.Icon, definition.Name, definition.Detail, groupStartsAt, expiresAt));

                var queuedBehind = Math.Max(0, queue.Count - 1);
                Console.WriteLine(groupDelay == 0
                    ? $"[OVERLAY][BUFF] active group={group}, key={definition.Key}, effect={effect}, detail='{definition.Detail}', duration={definition.DurationSeconds}s, queuedBehind={queuedBehind}"
                    : $"[OVERLAY][BUFF] queued group={group}, key={definition.Key}, effect={effect}, detail='{definition.Detail}', sharedStartsIn={groupDelay}s, queuePosition={queuedBehind}");
            }
        }
    }

    public void SetDonationRuntimeState(bool timersPaused, string reason)
    {
        string log;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            reason = string.IsNullOrWhiteSpace(reason) ? (timersPaused ? "PAUSED" : "READY") : reason;
            if (_buffTimersPaused == timersPaused)
            {
                _buffPauseReason = reason;
                return;
            }

            if (timersPaused)
            {
                _buffTimersPaused = true;
                _buffPauseStartedAt = now;
                _buffPauseReason = reason;
                log = $"[OVERLAY][BUFF-TIMER][PAUSED] reason={reason}, activeKeys={_buffQueues.Count}";
            }
            else
            {
                var pausedFor = _buffPauseStartedAt.HasValue ? now - _buffPauseStartedAt.Value : TimeSpan.Zero;
                if (pausedFor > TimeSpan.Zero)
                {
                    foreach (var queue in _buffQueues.Values)
                    {
                        for (var i = 0; i < queue.Count; i++)
                        {
                            var item = queue[i];
                            queue[i] = item with
                            {
                                StartsAt = item.StartsAt + pausedFor,
                                ExpiresAt = item.ExpiresAt + pausedFor
                            };
                        }
                    }
                }

                _buffTimersPaused = false;
                _buffPauseStartedAt = null;
                _buffPauseReason = reason;
                log = $"[OVERLAY][BUFF-TIMER][RESUMED] pausedForMs={pausedFor.TotalMilliseconds:F0}, activeKeys={_buffQueues.Count}";
            }
        }

        Console.WriteLine(log);
    }

    private DateTimeOffset GetBuffNowLocked()
        => _buffTimersPaused && _buffPauseStartedAt.HasValue ? _buffPauseStartedAt.Value : DateTimeOffset.UtcNow;

    private static List<BuffDefinition> GetBuffDefinitions(string effect, string eventName)
    {
        var result = new List<BuffDefinition>();
        switch (effect)
        {
            case "DUNGEON_SPEED_SMALL":
                result.Add(new("speed", "💨", eventName, "이동속도 +15%", 8));
                break;
            case "DUNGEON_SPEED_MEDIUM":
                result.Add(new("speed", "💨", eventName, "이동속도 +20%", 10));
                break;
            case "DUNGEON_SPEED_DOWN_SMALL":
                result.Add(new("speed", "🐌", eventName, "이동속도 -15%", 8));
                break;
            case "DUNGEON_SPEED_DOWN_MEDIUM":
                result.Add(new("speed", "🐌", eventName, "이동속도 -20%", 10));
                break;
            case "DUNGEON_ATTACK_MEDIUM":
                result.Add(new("attack", "⚔", eventName, "공격력 +20%", 10));
                break;
            case "DUNGEON_ATTACK_DOWN_MEDIUM":
                result.Add(new("attack", "🥀", eventName, "공격력 -20%", 10));
                break;
            case "DUNGEON_SPEED_ATTACK_LARGE":
                result.Add(new("speed", "💨", eventName, "이동속도 +25%", 12));
                result.Add(new("attack", "⚔", eventName, "공격력 +25%", 12));
                break;
            case "DUNGEON_SPEED_ATTACK_SPECIAL":
                result.Add(new("speed", "💨", eventName, "이동속도 +40%", 15));
                result.Add(new("attack", "⚔", eventName, "공격력 +40%", 15));
                break;
            case "DUNGEON_SPEED_ATTACK_DOWN_LARGE":
                result.Add(new("speed", "🐌", eventName, "이동속도 -25%", 12));
                result.Add(new("attack", "🥀", eventName, "공격력 -25%", 12));
                break;
            case "DUNGEON_SPEED_ATTACK_DOWN_SPECIAL":
                result.Add(new("speed", "🐌", eventName, "이동속도 -40%", 15));
                result.Add(new("attack", "🥀", eventName, "공격력 -40%", 15));
                break;
        }
        return result;
    }

    private OverlayState Snapshot()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var buffNow = GetBuffNowLocked();
            AdvanceTransientPhaseLocked(now);

            foreach (var entry in _buffQueues.ToArray())
            {
                var before = entry.Value.Count;
                entry.Value.RemoveAll(x => x.ExpiresAt <= buffNow);
                if (before != entry.Value.Count)
                    Console.WriteLine($"[OVERLAY][BUFF] advanced key={entry.Key}, expired={before - entry.Value.Count}, remainingQueue={entry.Value.Count}");
                if (entry.Value.Count == 0)
                {
                    _buffQueues.Remove(entry.Key);
                    _buffOrder.Remove(entry.Key);
                }
            }

            var remainingMs = _phase == "raffle" && _endsAt.HasValue
                ? Math.Max(0, (long)Math.Ceiling((_endsAt.Value - now).TotalMilliseconds))
                : 0L;

            var buffs = _buffOrder
                .Where(key => _buffQueues.ContainsKey(key))
                .Select(key =>
                {
                    var queue = _buffQueues[key];
                    var active = queue.FirstOrDefault(b => b.StartsAt <= buffNow && b.ExpiresAt > buffNow);
                    if (active is null) return null;
                    var queuedCount = queue.Count(b => b.StartsAt > buffNow);
                    return new OverlayBuffState(
                        active.Key, active.Icon, active.Name, active.Detail,
                        Math.Max(0, (long)Math.Ceiling((active.ExpiresAt - buffNow).TotalMilliseconds)),
                        queuedCount);
                })
                .Where(x => x is not null)
                .Select(x => x!)
                .ToArray();

            return new OverlayState(
                _phase,
                _command,
                remainingMs,
                _participantCount,
                _winnerNickname,
                _donationNickname,
                _donationAmount,
                _donationEventName,
                _donationQueue.Count,
                buffs,
                _buffTimersPaused,
                _buffPauseReason);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _listener is not null)
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[OVERLAY] server stopped: {ex.Message}");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                string? line;
                do { line = await reader.ReadLineAsync(ct); }
                while (!string.IsNullOrEmpty(line));

                var parts = requestLine.Split(' ');
                var target = parts.Length >= 2 ? parts[1] : "/";
                var path = target.Split('?', 2)[0];

                if (path.Equals("/overlay/state", StringComparison.OrdinalIgnoreCase))
                {
                    bool firstPoll;
                    lock (_gate)
                    {
                        firstPoll = _stateRequests == 0;
                        _stateRequests++;
                        _lastStateRequestAt = DateTimeOffset.UtcNow;
                    }
                    if (firstPoll)
                        Console.WriteLine($"[OVERLAY][CLIENT] state polling active: remote={client.Client.RemoteEndPoint}");
                    var json = JsonSerializer.Serialize(Snapshot(), new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                    await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", json, ct,
                        "Cache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\n");
                    return;
                }

                if (path.Equals("/overlay", StringComparison.OrdinalIgnoreCase) || path.Equals("/", StringComparison.OrdinalIgnoreCase))
                {
                    long pageRequest;
                    lock (_gate) pageRequest = ++_overlayPageRequests;
                    Console.WriteLine($"[OVERLAY][CLIENT] page loaded: request={pageRequest}, remote={client.Client.RemoteEndPoint}");
                    await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", OverlayHtml, ct,
                        "Cache-Control: no-store\r\n");
                    return;
                }

                await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not found", ct);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[OVERLAY] request failed: {ex.Message}");
            }
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string status, string contentType, string body,
        CancellationToken ct, string extraHeaders = "")
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n{extraHeaders}\r\n");
        await stream.WriteAsync(headers, ct);
        await stream.WriteAsync(payload, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { }
        }
        _cts.Dispose();
        _cts = null;
        _listener = null;
    }

    private sealed record OverlayState(
        string Phase,
        string Command,
        long RemainingMs,
        int ParticipantCount,
        string? WinnerNickname,
        string? DonationNickname,
        long DonationAmount,
        string? DonationEventName,
        int PendingDonationCount,
        OverlayBuffState[] ActiveBuffs,
        bool BuffTimersPaused,
        string BuffPauseReason);

    private sealed record OverlayBuffState(
        string Key,
        string Icon,
        string Name,
        string Detail,
        long RemainingMs,
        int QueuedCount);

    private sealed record ScheduledOverlayBuff(
        string Key,
        string Icon,
        string Name,
        string Detail,
        DateTimeOffset StartsAt,
        DateTimeOffset ExpiresAt);

    private sealed record DonationOverlayItem(
        long Sequence,
        string Nickname,
        long Amount,
        string EventName,
        TimeSpan DisplayDuration);

    private sealed record BuffDefinition(
        string Key,
        string Icon,
        string Name,
        string Detail,
        int DurationSeconds);

    private const string OverlayHtml = """
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>My Lamb Raffle Overlay</title>
<style>
  :root { color-scheme: dark; }
  html,body{margin:0;width:100%;height:100%;overflow:hidden;background:transparent;font-family:"Malgun Gothic","Noto Sans KR",sans-serif}
  body{display:flex;align-items:flex-start;justify-content:center;box-sizing:border-box;padding:18px}
  #stage{width:min(760px,calc(100vw - 36px));display:flex;flex-direction:column;align-items:center;gap:10px}
  #wrap{width:min(720px,100%);opacity:0;transform:translateY(-14px) scale(.98);transition:opacity .22s ease,transform .22s ease;pointer-events:none}
  #wrap.show{opacity:1;transform:translateY(0) scale(1)}
  #wrap.donationView{position:fixed!important;left:18px!important;right:auto!important;top:18px!important;width:min(480px,calc(100vw - 36px))!important;margin:0!important}
  .panel{position:relative;background:rgba(12,9,15,.90);border:2px solid rgba(248,235,207,.78);border-radius:22px;padding:18px 24px 16px;box-shadow:0 10px 34px rgba(0,0,0,.45),inset 0 0 0 1px rgba(255,255,255,.05)}
  .eyebrow{font-size:17px;font-weight:800;letter-spacing:.08em;color:#e7d5b0;text-align:center}
  .main{display:flex;align-items:center;justify-content:center;gap:22px;margin-top:6px}
  .command{font-size:34px;font-weight:900;color:#fff;white-space:nowrap;text-shadow:0 3px 10px #000}
  .command b{color:#ff5046;font-size:40px}
  .timer{min-width:118px;text-align:center;font-variant-numeric:tabular-nums;font-size:48px;line-height:1;font-weight:1000;color:#fff3d2;text-shadow:0 0 16px rgba(255,80,70,.30)}
  .meta{display:flex;justify-content:center;gap:18px;margin-top:12px;font-size:18px;color:#ddd1bd;font-weight:700}
  .progress{height:7px;margin-top:13px;background:rgba(255,255,255,.12);border-radius:999px;overflow:hidden}
  .bar{height:100%;width:100%;background:linear-gradient(90deg,#ff5046,#ffb64a);transform-origin:left;transition:transform .16s linear}
  .urgent .timer{color:#ff554c;animation:pulse .7s infinite alternate}
  .result{text-align:center;padding:4px 0}
  .resultTitle{font-size:18px;color:#e7d5b0;font-weight:800;letter-spacing:.08em}
  .winner{font-size:43px;font-weight:1000;color:#fff3d2;margin-top:5px;text-shadow:0 4px 13px #000}
  .donor{font-size:28px;font-weight:900;color:#fff;margin-top:4px}
  .donationEvent{font-size:36px;font-weight:1000;color:#00c471;margin-top:6px;text-shadow:0 3px 12px #000}
  .donationQueue{font-size:15px;font-weight:800;color:#d8cdbb;margin-top:8px}
  #buffs{position:fixed!important;left:18px!important;right:auto!important;top:18px;width:calc(100vw - 36px)!important;margin:0!important;transform:none!important;direction:ltr;display:flex;flex-direction:row;justify-content:flex-start!important;align-items:flex-start;align-content:flex-start;gap:8px;flex-wrap:wrap;pointer-events:none}
  .buff{min-width:168px;display:grid;grid-template-columns:42px 1fr;column-gap:9px;align-items:center;background:rgba(12,9,15,.88);border:1px solid rgba(248,235,207,.62);border-radius:14px;padding:8px 11px;box-shadow:0 6px 20px rgba(0,0,0,.38)}
  .buffIcon{grid-row:1/3;font-size:30px;line-height:1;text-align:center;filter:drop-shadow(0 2px 4px #000)}
  .buffName{font-size:14px;line-height:1.15;font-weight:900;color:#fff3d2;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  .buffMeta{display:flex;gap:7px;align-items:baseline;margin-top:3px;font-size:13px;font-weight:800;color:#d8cdbb}
  .buffTime{color:#00c471;font-variant-numeric:tabular-nums;font-size:15px}
  @keyframes pulse{from{transform:scale(1)}to{transform:scale(1.08)}}
</style>
</head>
<body>
<div id="stage"><div id="wrap"><div class="panel" id="panel"></div></div></div><div id="buffs"></div>
<script>
const wrap=document.getElementById('wrap');
const panel=document.getElementById('panel');
const buffs=document.getElementById('buffs');
let durationMs=30000,lastPhase='hidden';
function esc(s){return String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}
function applyOverlayLayout(phase){
  const donationMode=phase==='donation';
  if(donationMode){
    wrap.style.position='fixed';
    wrap.style.left='18px';
    wrap.style.right='auto';
    wrap.style.top='18px';
    wrap.style.width='min(480px,calc(100vw - 36px))';
    wrap.style.margin='0';
  }else{
    for(const property of ['position','left','right','top','width','margin']) wrap.style.removeProperty(property);
  }
  buffs.style.position='fixed';
  buffs.style.left='18px';
  buffs.style.right='auto';
  buffs.style.width='calc(100vw - 36px)';
  buffs.style.margin='0';
  buffs.style.transform='none';
  buffs.style.direction='ltr';
  buffs.style.flexDirection='row';
  buffs.style.justifyContent='flex-start';
  buffs.style.top=donationMode?`${Math.ceil(18+wrap.getBoundingClientRect().height+10)}px`:'18px';
}
function render(s){
  const phase=s.phase||'hidden';
  wrap.classList.toggle('show',phase!=='hidden');
  wrap.classList.toggle('donationView',phase==='donation');
  wrap.classList.remove('urgent');
  if(phase==='raffle'){
    if(lastPhase!=='raffle' && s.remainingMs>0) durationMs=s.remainingMs;
    const sec=Math.max(0,Math.ceil((s.remainingMs||0)/1000));
    const mm=String(Math.floor(sec/60)).padStart(2,'0'), ss=String(sec%60).padStart(2,'0');
    const ratio=Math.max(0,Math.min(1,(s.remainingMs||0)/Math.max(1,durationMs)));
    if(sec<=5) wrap.classList.add('urgent');
    panel.innerHTML=`<div class="eyebrow">새 신도 모집 중</div><div class="main"><div class="command">채팅에 <b>${esc(s.command)}</b></div><div class="timer">${mm}:${ss}</div></div><div class="meta"><span>참가자 ${Number(s.participantCount||0)}명</span><span>한 계정당 1회 참가</span></div><div class="progress"><div class="bar" style="transform:scaleX(${ratio})"></div></div>`;
  } else if(phase==='winner'){
    panel.innerHTML=`<div class="result"><div class="resultTitle">신도 당첨자</div><div class="winner">${esc(s.winnerNickname||'당첨자')}</div></div>`;
  } else if(phase==='empty'){
    panel.innerHTML=`<div class="result"><div class="resultTitle">신도 모집 종료</div><div class="winner">참가자가 없습니다</div></div>`;
  } else if(phase==='cancelled'){
    panel.innerHTML=`<div class="result"><div class="resultTitle">신도 모집</div><div class="winner">취소되었습니다</div></div>`;
  } else if(phase==='donation'){
    const amount=Number(s.donationAmount||0).toLocaleString('ko-KR');
    const pending=Number(s.pendingDonationCount||0);
    const queue=pending>0?`<div class="donationQueue">다음 후원 이벤트 ${pending}건 대기 중</div>`:'';
    panel.innerHTML=`<div class="result"><div class="resultTitle">CHZZK 후원 이벤트</div><div class="donor">${esc(s.donationNickname||'후원자')} · ${amount}원</div><div class="donationEvent">${esc(s.donationEventName||'이벤트 발동')}</div>${queue}</div>`;
  }
  applyOverlayLayout(phase);
  const active=Array.isArray(s.activeBuffs)?s.activeBuffs:[];
  buffs.innerHTML=active.map(b=>{
    const sec=Math.max(0,Math.ceil(Number(b.remainingMs||0)/1000));
    const queued=Number(b.queuedCount||0);
    const queueText=queued>0?` · 대기 ${queued}`:'';
    const timer=s.buffTimersPaused?'일시정지':`${sec}s`;
    return `<div class="buff"><div class="buffIcon">${esc(b.icon||'✦')}</div><div class="buffName">${esc(b.name||'후원 버프')}${queueText}</div><div class="buffMeta"><span>${esc(b.detail||'')}</span><span class="buffTime">${timer}</span></div></div>`;
  }).join('');
  lastPhase=phase;
}
async function tick(){
  try{const r=await fetch('/overlay/state',{cache:'no-store'});if(r.ok)render(await r.json())}catch(e){}
}
setInterval(tick,200);tick();
</script>
</body>
</html>
""";
}
