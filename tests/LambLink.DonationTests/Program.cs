using System.Reflection;
using System.Text.Json;
using BepInEx.Logging;
using LambLink.Companion.Chzzk;
using LambLink.Companion.Configuration;
using LambLink.Companion.Diagnostics;
using LambLink.Companion.Rules;
using LambLink.Companion.Storage;
using LambLink.Mod.Game;
using LambLink.Protocol;

var root = Path.Combine(Path.GetTempPath(), "LambLink-DonationTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS: " + name); }
void Throws(Action action) { try { action(); } catch (Exception) { return; } throw new Exception("Expected failure."); }
string Payload(string amount, string type = "CHAT") => "{\"donationType\":\"" + type + "\",\"channelId\":\"streamer\",\"donatorChannelId\":\"anonymous\",\"donatorNickname\":\"\",\"payAmount\":" + amount + ",\"donationText\":\"private-message\",\"eventSentAt\":\"2026-07-26T03:14:28.690447802\",\"emojis\":{}}";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    Test("CHAT/VIDEO numbers and strings normalize identically; no donor identity invented", () => {
        foreach (var type in new[] { "CHAT", "VIDEO" })
        foreach (var amount in new[] { "1", "999", "1000", "2999", "3000", "4999", "5000", "9999", "10000", long.MaxValue.ToString() })
        foreach (var token in new[] { amount, "\"" + amount + "\"" })
        {
            var result = DonationEventParser.Parse(Payload(token, type));
            Check(result.Success && result.Donation!.TryGetAmount(out var value, out _) && value == long.Parse(amount), token);
            Check(result.Donation!.DonatorNickname == "" && result.Donation.DonatorChannelId == "anonymous", "anonymous fields");
        }
    });
    Test("invalid amounts rejected without rounding, defaults, or diagnostic input leaks", () => {
        foreach (var token in new[] { "0", "-1", "1.0", "1.5", "1e3", "9223372036854775808", "null", "true", "[]", "{}", "\"\"", "\"1,000\"", "\"1000원\"", "\"+1000\"", "\"private-secret\"" })
        {
            var result = DonationEventParser.Parse(Payload(token));
            Check(!result.Success && result.Field == "payAmount", token);
            Check(!JsonSerializer.Serialize(result).Contains("private-"), "diagnostic leak");
        }
    });
    Test("event envelopes, unknown fields, missing optional fields, duplicates and payload bounds", () => {
        var payload = Payload("1000");
        Check(DonationEventParser.Parse(JsonSerializer.Serialize(payload)).Success, "string argument");
        Check(DonationEventParser.Parse("{\"data\":" + payload + "}").Success, "data object");
        Check(!DonationEventParser.Parse("{\"data\":" + JsonSerializer.Serialize(payload) + "}").Success, "unobserved data string");
        Check(DonationEventParser.Parse("{\"channelId\":\"s\",\"donationType\":\"CHAT\",\"payAmount\":1000}").Success, "optional fields");
        Check(!DonationEventParser.Parse(payload.Replace("\"payAmount\":1000", "\"payAmount\":1000,\"payAmount\":2000")).Success, "duplicate");
        Check(!DonationEventParser.Parse(payload.Replace("\"channelId\":\"streamer\",", "")).Success, "required channel");
        Check(DonationEventParser.Parse(Payload("1000", "MISSION")).Code == "UNSUPPORTED_DONATION_TYPE", "unsupported type");
        Check(DonationEventParser.Parse("[]").Code == "INVALID_ROOT", "root");
        Check(DonationEventParser.Parse("{").Code == "INVALID_JSON", "json");
        Check(DonationEventParser.Parse(new string('x', 65537)).Code == "PAYLOAD_TOO_LARGE", "limit");
    });
    Test("production reception continues after invalid donation, assigns unique correlation, preserves chat parser", () => {
        var client = new ChzzkRealtimeClient(null!);
        var observations = new List<DonationReception>();
        var delivered = new List<DonationEvent>();
        client.DonationObserved += observations.Add;
        client.Donation += delivered.Add;
        client.ProcessDonationPayload(Payload("true"));
        client.ProcessDonationPayload(Payload("1000"));
        client.ProcessDonationPayload(Payload("\"1000\""));
        Check(observations.Count == 3 && delivered.Count == 2, "counts");
        Check(observations.Select(x => x.Id).Distinct().Count() == 3 && delivered[0].ReceptionId == observations[1].Id, "ids");
        var parser = typeof(ChzzkRealtimeClient).GetMethod("DeserializeEventPayload", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(parser.MakeGenericMethod(typeof(ChatEvent)).Invoke(client, new object[]{"{\"channelId\":\"s\",\"senderChannelId\":\"v\",\"content\":\"hi\",\"messageTime\":1}"}) is ChatEvent, "chat");
        Check(parser.MakeGenericMethod(typeof(SubscriptionEvent)).Invoke(client, new object[]{"{\"channelId\":\"s\",\"tierNo\":1,\"month\":2}"}) is SubscriptionEvent, "subscription");
    });
    Test("rules use correct amount tier and area, disabled rules exclude", () => {
        var rules = new DonationRuleEngine(new DonationSettings());
        foreach (var (amount, tier) in new (long,string)[]{(1,"NONE"),(999,"NONE"),(1000,"SMALL"),(2999,"SMALL"),(3000,"MEDIUM"),(4999,"MEDIUM"),(5000,"HELP_OR_HINDER"),(9999,"HELP_OR_HINDER"),(10000,"SPECIAL")})
        {
            for(var i=0;i<20;i++)
            {
                Check(rules.ResolveDecision(amount,"BASE").TierName == tier,"base boundary");
                Check(rules.ResolveDecision(amount,"DUNGEON").TierName == (tier=="NONE"?tier:"DUNGEON_"+tier),"dungeon boundary");
            }
        }
        Check(new DonationRuleEngine(new DonationSettings{Enabled=false}).ResolveDecision(10000,"DUNGEON").Effect=="NONE","disabled");
    });
    Test("outbox survives restart and same ID is never re-enqueued", () => {
        var path=Path.Combine(root,"outbox.json");
        var repo=new DonationDeliveryRepository(path);
        var item=new DonationDeliveryRecord{RequestId=Guid.NewGuid().ToString("N"),Amount=1000};
        Check(repo.TryAdd(item) && !repo.TryAdd(item),"duplicate");
        repo=new DonationDeliveryRepository(path);
        Check(repo.Snapshot().Single().RequestId==item.RequestId,"restart");
        Directory.CreateDirectory(path+".tmp");
        Throws(()=>repo.TryAdd(new DonationDeliveryRecord{RequestId=Guid.NewGuid().ToString("N")}));
        Throws(()=>repo.TryRecordAttempt(item.RequestId,DateTimeOffset.UtcNow));
        Throws(()=>repo.TryComplete(item.RequestId,out _));
        Check(repo.PendingCount==1 && repo.Snapshot().Single().AttemptCount==0,"memory rollback");
        Check(new DonationDeliveryRepository(path).PendingCount==1,"disk preserved");
        Directory.Delete(path+".tmp");
        repo.TryComplete(item.RequestId,out _);
        Check(repo.PendingCount==0 && new DonationDeliveryRepository(path).PendingCount==0,"completion");
    });
    Test("corrupt outbox is preserved and blocks new paid deliveries", () => {
        var path=Path.Combine(root,"broken-outbox.json"); File.WriteAllText(path,"broken");
        var repo=new DonationDeliveryRepository(path);
        Throws(()=>repo.TryAdd(new DonationDeliveryRecord{RequestId=Guid.NewGuid().ToString("N")}));
        Check(repo.RecoveryError!="none" && File.ReadAllText(path)=="broken","fail closed");
    });
    Test("receipt failed terminal save preserves runtime dedup; restart becomes uncertain", () => {
        var path=Path.Combine(root,"receipts.json"); var log=new ManualLogSource("DonationTest");
        var store=new DonationReceiptStore(path,log);
        var command=new DonationEffectCommand("viewer","test",1000,"SMALL_FAITH_UP_5",null){ReceivedAtUnixMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()};
        store.MarkPending(command); store.MarkApplying(command);
        Directory.CreateDirectory(path+".tmp");
        Throws(()=>store.MarkCompleted(new DonationEffectResult{RequestId=command.RequestId,Success=true,Amount=1000}));
        Check(store.TryGetTerminalResult(command.RequestId,out var live) && live.Success,"runtime duplicate");
        Directory.Delete(path+".tmp");
        store=new DonationReceiptStore(path,log);
        Check(store.TryGetTerminalResult(command.RequestId,out var restored) && !restored.Success && restored.StatusCode=="APPLICATION_UNCERTAIN","crash recovery");
    });
    Test("receipt pending persistence failure rolls back; corruption blocks application", () => {
        var path=Path.Combine(root,"pending-receipt.json"); var log=new ManualLogSource("DonationTest");
        var store=new DonationReceiptStore(path,log); var command=new DonationEffectCommand("v","n",1000,"E",null);
        Directory.CreateDirectory(path+".tmp"); Throws(()=>store.MarkPending(command)); Directory.Delete(path+".tmp");
        Throws(()=>store.MarkApplying(command));
        File.WriteAllText(path,"broken"); Throws(()=>new DonationReceiptStore(path,log));
        Check(File.ReadAllText(path)=="broken","corrupt preserved");
    });
    Test("expired replay history rejects unknown older requests, accepts retained and new requests", () => {
        var path=Path.Combine(root,"cutoff.json");
        File.WriteAllText(path,"{\"Version\":2,\"ReplayCutoffUnixMs\":1000,\"Receipts\":[]}");
        var store=new DonationReceiptStore(path,new ManualLogSource("DonationTest"));
        var old=new DonationEffectCommand("v","n",1000,"E",null){ReceivedAtUnixMs=1000};
        Throws(()=>store.MarkPending(old));
        var fresh=new DonationEffectCommand("v","n",1000,"E",null){ReceivedAtUnixMs=1001};
        store.MarkPending(fresh); store.MarkApplying(fresh); store.MarkCompleted(new DonationEffectResult{RequestId=fresh.RequestId,Success=true});
        Check(new DonationReceiptStore(path,new ManualLogSource("DonationTest")).TryGetTerminalResult(fresh.RequestId,out var result)&&result.Success,"retained duplicate");
    });
    Test("operational history is queryable without overlay and contains no donor data", () => {
        var path=Path.Combine(root,"history.json"); var history=new DonationActivityLog(path);
        var id=Guid.NewGuid().ToString("N");
        history.Observe(new(id,"session",1,DateTimeOffset.UtcNow,100,DonationEventParser.Parse(Payload("1000"))));
        history.Update(id,"QUEUED","DURABLY_STORED","SMALL_FAITH_UP_5");
        history.Update(id,"APPLIED","MOD_ACK"); history.Update(id,"AWAITING_RESULT","late transport callback");
        history.Display(id,"PAGE_CONFIRMED");
        var restored=new DonationActivityLog(path);
        Check(restored.Recent().Single().Contains("stage=APPLIED") && restored.Recent().Single().Contains("PAGE_CONFIRMED"),"history");
        Check(!File.ReadAllText(path).Contains("private-message") && !File.ReadAllText(path).Contains("anonymous"),"history privacy");
    });
    Test("socket event frames accept object and string arguments and reject malformed frames", () => {
        foreach (var argument in new[] { Payload("1000"), JsonSerializer.Serialize(Payload("1000")) })
        {
            Check(ChzzkRealtimeClient.TryDecodeSocketEvent("42[\"DONATION\"," + argument + "]", out var name, out var payload), "decode");
            Check(name == "DONATION" && DonationEventParser.Parse(payload).Success, "frame payload");
        }
        foreach (var frame in new[] { "42[", "42[]", "42[1,{}]" })
            Check(!ChzzkRealtimeClient.TryDecodeSocketEvent(frame, out _, out _), "malformed frame");
    });
    Test("version one receipt migration preserves original backup and new result", () => {
        var path=Path.Combine(root,"legacy-receipts.json");
        var original="{\"Version\":1,\"Receipts\":[]}";
        File.WriteAllText(path,original);
        var store=new DonationReceiptStore(path,new ManualLogSource("DonationTest"));
        var command=new DonationEffectCommand("v","n",1000,"E",null){ReceivedAtUnixMs=1001};
        store.MarkPending(command); store.MarkApplying(command);
        store.MarkCompleted(new DonationEffectResult{RequestId=command.RequestId,Success=true});
        Check(File.ReadAllText(path+".v1.bak")==original,"backup");
        Check(new DonationReceiptStore(path,new ManualLogSource("DonationTest")).TryGetTerminalResult(command.RequestId,out var result)&&result.Success,"migrated result");
    });
    Test("restart restores unconfirmed displays and marks orphan deliveries uncertain", () => {
        var path=Path.Combine(root,"display-history.json"); var history=new DonationActivityLog(path);
        var applied=Guid.NewGuid().ToString("N"); var orphan=Guid.NewGuid().ToString("N");
        foreach(var id in new[]{applied,orphan})
            history.Observe(new(id,"session",1,DateTimeOffset.UtcNow,100,DonationEventParser.Parse(Payload("1000"))));
        history.Update(applied,"APPLIED","MOD_ACK"); history.Display(applied,"WAITING_CLIENT");
        history=new DonationActivityLog(path); history.ReconcilePending(new HashSet<string>());
        Check(history.PendingDisplays().Single().Id==applied,"display recovery");
        Check(history.Recent().Any(x=>x.Contains(orphan)&&x.Contains("stage=UNCERTAIN")),"orphan");
        history.Display(applied,"PAGE_CONFIRMED");
        Check(new DonationActivityLog(path).PendingDisplays().Length==0,"confirmed not replayed");
        var stale=Guid.NewGuid().ToString("N");
        File.WriteAllText(path,JsonSerializer.Serialize(new[]{new DonationActivity(stale,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddHours(-1),1000,"APPLIED","MOD_ACK","E","WAITING_CLIENT",DateTimeOffset.UtcNow.AddHours(-1))},jsonOptions));
        history=new DonationActivityLog(path); history.ReconcilePending(new HashSet<string>());
        Check(history.PendingDisplays().Length==0 && history.Recent().Single().Contains("EXPIRED_AGE"),"expired recovery");
    });
    Console.WriteLine($"SUCCESS: {passed} test groups");
}
finally { Directory.Delete(root,recursive:true); }
