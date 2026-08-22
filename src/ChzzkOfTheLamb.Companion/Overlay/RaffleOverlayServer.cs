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

    public RaffleOverlayServer(int port = 17883)
    {
        _port = port;
    }

    public string OverlayUrl => $"http://127.0.0.1:{_port}/overlay";

    public void Start()
    {
        if (_listener is not null) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        Console.WriteLine($"[OVERLAY] OBS browser source: {OverlayUrl}");
    }

    public void Open(int durationSeconds, string command)
    {
        lock (_gate)
        {
            _phase = "raffle";
            _command = string.IsNullOrWhiteSpace(command) ? "!신도" : command;
            _endsAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, durationSeconds));
            _visibleUntil = null;
            _participantCount = 0;
            _winnerNickname = null;
        }
    }

    public void SetParticipantCount(int count)
    {
        lock (_gate) _participantCount = Math.Max(0, count);
    }

    public void ShowWinner(string nickname, int seconds = 5)
    {
        lock (_gate)
        {
            _phase = "winner";
            _endsAt = null;
            _visibleUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, seconds));
            _winnerNickname = nickname;
        }
    }

    public void ShowNoParticipants(int seconds = 3)
    {
        lock (_gate)
        {
            _phase = "empty";
            _endsAt = null;
            _visibleUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, seconds));
            _winnerNickname = null;
        }
    }

    public void ShowCancelled(int seconds = 2)
    {
        lock (_gate)
        {
            _phase = "cancelled";
            _endsAt = null;
            _visibleUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, seconds));
            _winnerNickname = null;
        }
    }


    public void ShowDonation(string nickname, long amount, string eventName, int seconds = 5)
    {
        lock (_gate)
        {
            // Do not hide an active raffle countdown. Donation execution still happens and
            // is fully logged; the visual card is skipped only while the raffle is on screen.
            if (_phase == "raffle") return;

            _phase = "donation";
            _endsAt = null;
            _visibleUntil = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, seconds));
            _donationNickname = nickname;
            _donationAmount = Math.Max(0, amount);
            _donationEventName = eventName;
        }
    }

    private OverlayState Snapshot()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_phase != "raffle" && _visibleUntil.HasValue && now >= _visibleUntil.Value)
            {
                _phase = "hidden";
                _visibleUntil = null;
                _winnerNickname = null;
                _donationNickname = null;
                _donationAmount = 0;
                _donationEventName = null;
            }

            var remainingMs = _phase == "raffle" && _endsAt.HasValue
                ? Math.Max(0, (long)Math.Ceiling((_endsAt.Value - now).TotalMilliseconds))
                : 0L;

            return new OverlayState(
                _phase,
                _command,
                remainingMs,
                _participantCount,
                _winnerNickname,
                _donationNickname,
                _donationAmount,
                _donationEventName);
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
        string? DonationEventName);

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
  #wrap{width:min(720px,calc(100vw - 36px));opacity:0;transform:translateY(-14px) scale(.98);transition:opacity .22s ease,transform .22s ease;pointer-events:none}
  #wrap.show{opacity:1;transform:translateY(0) scale(1)}
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
  @keyframes pulse{from{transform:scale(1)}to{transform:scale(1.08)}}
</style>
</head>
<body>
<div id="wrap"><div class="panel" id="panel"></div></div>
<script>
const wrap=document.getElementById('wrap');
const panel=document.getElementById('panel');
let durationMs=30000,lastPhase='hidden';
function esc(s){return String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}
function render(s){
  const phase=s.phase||'hidden';
  wrap.classList.toggle('show',phase!=='hidden');
  wrap.classList.remove('urgent');
  if(phase==='hidden') return;
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
    panel.innerHTML=`<div class="result"><div class="resultTitle">CHZZK 후원 이벤트</div><div class="donor">${esc(s.donationNickname||'후원자')} · ${amount}원</div><div class="donationEvent">${esc(s.donationEventName||'이벤트 발동')}</div></div>`;
  }
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
