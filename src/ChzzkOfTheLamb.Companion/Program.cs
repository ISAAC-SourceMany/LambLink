using System.Text;
using System.Text.Json;
using ChzzkOfTheLamb.Companion.Appearance;
using ChzzkOfTheLamb.Companion.Chzzk;
using ChzzkOfTheLamb.Companion.Cloud;
using ChzzkOfTheLamb.Companion.Configuration;
using ChzzkOfTheLamb.Companion.GameBridge;
using ChzzkOfTheLamb.Companion.Raffle;
using ChzzkOfTheLamb.Companion.Overlay;
using ChzzkOfTheLamb.Companion.Rules;
using ChzzkOfTheLamb.Companion.Storage;
using ChzzkOfTheLamb.Protocol;

ChzzkCredentials? chzzkCredentials = null;
try
{
    chzzkCredentials = ChzzkCredentialProvider.Load();
}
catch (Exception ex)
{
    Console.WriteLine($"[CONFIG] CHZZK credential provider failed: {ex.Message}");
}
var clientId = chzzkCredentials?.ClientId;
var clientSecret = chzzkCredentials?.ClientSecret;
var redirectUri = Environment.GetEnvironmentVariable("CHZZK_REDIRECT_URI")
                  ?? "http://127.0.0.1:17881/callback/";
var forceDevelopmentMode = IsTruthy(Environment.GetEnvironmentVariable("CHZZK_DEV_MODE"));

var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChzzkOfTheLamb");
Directory.CreateDirectory(dataDir);
var settings = CompanionSettings.LoadOrCreate(Path.Combine(dataDir, "settings.json"));
var followers = new ViewerFollowerRepository(Path.Combine(dataDir, "viewer-followers.json"));
var appearances = new AppearanceStore(Path.Combine(dataDir, "viewer-appearances.json"));
var raffle = new RaffleManager();
var rules = new DonationRuleEngine(settings.Donation);

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

await using var overlay = new RaffleOverlayServer();
overlay.Start();

var hasChzzkCredentials = !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
var developmentMode = forceDevelopmentMode || !hasChzzkCredentials;

Console.WriteLine("CHZZK Companion for Cult of the Lamb - v0.1-devbridge10w");
Console.WriteLine($"[CONFIG] companion credentials: {chzzkCredentials?.ProviderName ?? "not loaded"}");
if (developmentMode)
{
    Console.WriteLine("[MODE] DEVELOPMENT / OFFLINE CHZZK");
    if (!hasChzzkCredentials)
        Console.WriteLine("[CHZZK] credentials not set. CHZZK OAuth/realtime is disabled, but Game Bridge remains available.");
    else
        Console.WriteLine("[CHZZK] CHZZK_DEV_MODE is enabled. OAuth/realtime is intentionally disabled.");
}
else
{
    Console.WriteLine("[MODE] CHZZK LIVE");
}

var configuredWebApiBase = Environment.GetEnvironmentVariable("COTL_WEB_API_BASE");
if (!string.IsNullOrWhiteSpace(configuredWebApiBase))
    Console.WriteLine($"[WEB] API configured: {configuredWebApiBase}");
if (developmentMode && !string.IsNullOrWhiteSpace(configuredWebApiBase))
    Console.WriteLine("[WEB] My Lamb API is configured, but Companion authentication/catalog upload requires CHZZK LIVE mode.");

var streamerChannelId = settings.Development.LocalStreamerId;
var streamerChannelName = settings.Development.LocalStreamerName;
string? accessToken = null;
ChzzkApiClient? api = null;
HttpClient? http = null;
AppearanceApiClient? cloud = null;
var cloudConnectGate = new SemaphoreSlim(1, 1);
var cloudBaseUrl = configuredWebApiBase;
if (string.IsNullOrWhiteSpace(cloudBaseUrl) && settings.Cloud.Enabled)
    cloudBaseUrl = settings.Cloud.ApiBaseUrl;
var frontendUrl = Environment.GetEnvironmentVariable("COTL_WEB_FRONTEND_URL");
if (string.IsNullOrWhiteSpace(frontendUrl)) frontendUrl = settings.Cloud.FrontendUrl;

if (!developmentMode)
{
    http = new HttpClient();
    api = new ChzzkApiClient(http, clientId!, clientSecret!);

    Console.WriteLine("Opening browser for CHZZK OAuth...");
    var (code, state) = await LoopbackOAuth.AuthorizeAsync(api, redirectUri, stop.Token);
    var tokens = await api.ExchangeCodeAsync(code, state, stop.Token);
    var me = await api.GetMeAsync(tokens.AccessToken, stop.Token);

    accessToken = tokens.AccessToken;
    streamerChannelId = me.ChannelId;
    streamerChannelName = me.ChannelName;
    Console.WriteLine($"[CHZZK] connected: {streamerChannelName} ({streamerChannelId})");

    if (!string.IsNullOrWhiteSpace(cloudBaseUrl))
        await TryConnectCloudAsync(logFailure: true);
}

var currentSaveId = "unknown";
var latestCatalogCount = 0;
var latestCatalogSaveId = "unknown";
string? currentRaffleId = null;
int? currentRecruitFollowerId = null;
var pendingRaffleRequests = new Queue<RaffleRequestedEvent>();
var queuedRecruitIds = new HashSet<int>();
var raffleQueueGate = new object();
GameStatusEvent? lastGameStatus = null;
string? lastCatalogFingerprint = null;
var catalogRefreshInFlight = 0;
var latestRosterSaveId = "unknown";
var latestRosterFollowerIds = new HashSet<int>();
var latestRosterFollowersById = new Dictionary<int, FollowerRosterEntry>();
DateTimeOffset latestRosterAt = DateTimeOffset.MinValue;
string? lastRosterFingerprint = null;

await using var bridge = new GameBridgeServer();
bridge.ConnectionChanged += connected =>
{
    Console.WriteLine(connected ? "[GAME] connected" : "[GAME] disconnected");
    // Catalog discovery is intentionally deferred until GAME_STATUS reports InGame=true.
    // This keeps the mod from touching WorshipperData during the game's splash/bootstrap scene.
};

string lastNameplateSyncSignature = string.Empty;

bridge.MessageReceived += envelope =>
{
    try
    {
        switch (envelope.Type)
        {
            case GameMessageTypes.GameStatus:
            {
                var status = JsonSerializer.Deserialize<GameStatusEvent>(envelope.PayloadJson)!;
                var previousSave = currentSaveId;
                currentSaveId = status.SaveId;

                var changed = lastGameStatus is null
                              || lastGameStatus.InGame != status.InGame
                              || !string.Equals(lastGameStatus.SaveId, status.SaveId, StringComparison.Ordinal)
                              || !string.Equals(lastGameStatus.ModVersion, status.ModVersion, StringComparison.Ordinal)
                              || !string.Equals(lastGameStatus.GameVersion, status.GameVersion, StringComparison.Ordinal)
                              || !string.Equals(lastGameStatus.Area, status.Area, StringComparison.Ordinal);

                if (changed)
                    Console.WriteLine($"[GAME] inGame={status.InGame} save={status.SaveId} area={status.Area} mod={status.ModVersion} game={status.GameVersion}");

                // Refresh the catalog automatically when a save becomes available or changes.
                if (status.InGame && status.SaveId != "unknown"
                    && (!string.Equals(previousSave, status.SaveId, StringComparison.Ordinal)
                        || lastGameStatus?.InGame != true))
                {
                    _ = bridge.SendAsync(GameMessageTypes.GetAppearanceCatalog, new AppearanceCatalogRequest
                    {
                        IncludeModded = settings.Appearance.IncludeModdedForms,
                        IncludeSpecial = settings.Appearance.IncludeSpecialForms
                    }, stop.Token);
                    _ = bridge.SendAsync(GameMessageTypes.GetFollowerRoster, new { }, stop.Token);
                }

                lastGameStatus = status;
                break;
            }
            case GameMessageTypes.AppearanceCatalog:
            {
                var catalog = JsonSerializer.Deserialize<FollowerAppearanceCatalog>(envelope.PayloadJson)!;
                appearances.UpdateCatalog(catalog);
                latestCatalogCount = catalog.Forms.Count;
                latestCatalogSaveId = catalog.SaveId;
                // Catalog messages are emitted from the active save. Use them as a safe
                // synchronization source if the periodic GAME_STATUS has not caught up yet.
                if (!string.IsNullOrWhiteSpace(catalog.SaveId) && catalog.SaveId != "unknown")
                    currentSaveId = catalog.SaveId;
                var fingerprint = BuildCatalogFingerprint(catalog);
                var catalogChanged = !string.Equals(lastCatalogFingerprint, fingerprint, StringComparison.Ordinal);
                Console.WriteLine($"[APPEARANCE] loaded {catalog.Forms.Count} forms for save {catalog.SaveId}" +
                                  (catalogChanged ? " (changed)" : " (unchanged)"));

                if (catalogChanged)
                {
                    if (lastCatalogFingerprint is not null)
                        Console.WriteLine($"[APPEARANCE] catalog changed -> forms={catalog.Forms.Count}; uploading latest catalog.");
                    lastCatalogFingerprint = fingerprint;
                    if (cloud is not null && settings.Cloud.UploadCatalog)
                        _ = UploadLatestCatalogAsync();
                }
                break;
            }
            case GameMessageTypes.FollowerRoster:
            {
                var roster = JsonSerializer.Deserialize<FollowerRosterSnapshot>(envelope.PayloadJson)!;
                latestRosterSaveId = roster.SaveId;
                latestRosterAt = roster.GeneratedAt;
                latestRosterFollowerIds = roster.Followers.Select(x => x.FollowerId).Where(x => x > 0).ToHashSet();
                latestRosterFollowersById = roster.Followers
                    .Where(x => x.FollowerId > 0)
                    .GroupBy(x => x.FollowerId)
                    .ToDictionary(g => g.Key, g => g.First());

                var stale = new List<ViewerFollowerRecord>();
                if (!string.IsNullOrWhiteSpace(roster.SaveId) && roster.SaveId != "unknown")
                {
                    foreach (var record in followers.GetForSave(streamerChannelId, roster.SaveId))
                    {
                        if (!latestRosterFollowersById.TryGetValue(record.FollowerId, out var actual))
                        {
                            stale.Add(record);
                            continue;
                        }

                        if (!FollowerRosterNameMatchesViewer(actual.Name, record.LastKnownNickname))
                        {
                            stale.Add(record);
                            Console.WriteLine($"[FOLLOWER-RECONCILE] stale/reused ID detected from roster: viewer={record.LastKnownNickname} ({record.ViewerChannelId}), followerId={record.FollowerId}, actualName='{actual.Name}', save={roster.SaveId}");
                        }
                    }
                    foreach (var record in stale)
                    {
                        if (followers.Remove(streamerChannelId, record.ViewerChannelId, roster.SaveId))
                            Console.WriteLine($"[FOLLOWER-RECONCILE] removed stale mapping: viewer={record.LastKnownNickname} ({record.ViewerChannelId}), followerId={record.FollowerId}, save={roster.SaveId}; follower missing or identity no longer matches loaded save");
                    }
                }

                var rosterFingerprint = roster.SaveId + ":" + string.Join(",", latestRosterFollowerIds.OrderBy(x => x));
                if (stale.Count > 0 || !string.Equals(lastRosterFingerprint, rosterFingerprint, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[FOLLOWER-ROSTER] save={roster.SaveId}, actual={latestRosterFollowerIds.Count}, staleRemoved={stale.Count}, ids=[{string.Join(",", latestRosterFollowerIds.OrderBy(x => x))}]");
                    lastRosterFingerprint = rosterFingerprint;
                }
                _ = SyncChzzkMarkersToGameAsync(roster.SaveId, validateAgainstRoster: true);
                break;
            }
            case GameMessageTypes.FollowerSpawnResult:
            {
                var result = JsonSerializer.Deserialize<FollowerSpawnResult>(envelope.PayloadJson)!;
                if (result.Success && result.FollowerId.HasValue)
                {
                    followers.Upsert(new ViewerFollowerRecord
                    {
                        StreamerChannelId = streamerChannelId,
                        ViewerChannelId = result.ViewerId,
                        LastKnownNickname = result.Nickname,
                        SaveId = result.SaveId,
                        FollowerId = result.FollowerId.Value
                    });
                    if (string.Equals(latestRosterSaveId, result.SaveId, StringComparison.Ordinal)) latestRosterFollowerIds.Add(result.FollowerId.Value);
                    Console.WriteLine($"[FOLLOWER] created {result.Nickname}, ID={result.FollowerId}");
                    _ = SyncChzzkMarkersToGameAsync(result.SaveId, validateAgainstRoster: false);
                }
                else Console.WriteLine($"[FOLLOWER] create failed: {result.Error}");
                break;
            }
            case GameMessageTypes.RecruitIdentityResult:
            {
                var result = JsonSerializer.Deserialize<RecruitIdentityResult>(envelope.PayloadJson)!;
                if (result.Success)
                {
                    followers.Upsert(new ViewerFollowerRecord
                    {
                        StreamerChannelId = streamerChannelId,
                        ViewerChannelId = result.ViewerId,
                        LastKnownNickname = result.Nickname,
                        SaveId = result.SaveId,
                        FollowerId = result.RecruitFollowerId
                    });
                    if (string.Equals(latestRosterSaveId, result.SaveId, StringComparison.Ordinal)) latestRosterFollowerIds.Add(result.RecruitFollowerId);
                    Console.WriteLine($"[RAFFLE] identity applied: {result.Nickname} -> recruit {result.RecruitFollowerId}");
                    _ = SyncChzzkMarkersToGameAsync(result.SaveId, validateAgainstRoster: false);
                }
                else Console.WriteLine($"[RAFFLE] identity apply failed: {result.Error}");

                currentRecruitFollowerId = null;
                StartNextQueuedRaffle();
                break;
            }
            case GameMessageTypes.DonationEffectResult:
            {
                var result = JsonSerializer.Deserialize<DonationEffectResult>(envelope.PayloadJson)!;
                if (result.Success)
                {
                    Console.WriteLine($"[DONATION][RESULT] request={ShortId(result.RequestId)} SUCCESS event='{result.EventName}' effect={result.Effect}; {result.Details}");
                    overlay.ShowDonation(result.Nickname, result.Amount, result.EventName, seconds: 5);
                    overlay.RegisterDonationBuff(result.Effect, result.EventName);
                }
                else
                {
                    Console.WriteLine($"[DONATION][RESULT] request={ShortId(result.RequestId)} FAILED event='{result.EventName}' effect={result.Effect}; error={result.Error}");
                }
                break;
            }
            case GameMessageTypes.RaffleRequested:
            {
                var request = JsonSerializer.Deserialize<RaffleRequestedEvent>(envelope.PayloadJson)!;
                if (request.RecruitFollowerId <= 0) break;

                var startNow = false;
                lock (raffleQueueGate)
                {
                    if (currentRecruitFollowerId == request.RecruitFollowerId || queuedRecruitIds.Contains(request.RecruitFollowerId))
                        break;

                    if (currentRecruitFollowerId.HasValue || raffle.IsOpen)
                    {
                        pendingRaffleRequests.Enqueue(request);
                        queuedRecruitIds.Add(request.RecruitFollowerId);
                        Console.WriteLine($"[RAFFLE] queued game recruit {request.RecruitFollowerId}; queue={pendingRaffleRequests.Count}");
                    }
                    else startNow = true;
                }
                if (startNow) BeginRaffleFor(request);
                break;
            }
        }
    }
    catch (Exception ex) { Console.WriteLine($"[Bridge] invalid message: {ex.Message}"); }
};

raffle.Started += (id, seconds) =>
{
    currentRaffleId = id;
    Console.WriteLine($"[RAFFLE] OPEN {seconds}s — chat: {settings.Raffle.JoinCommand}");
    overlay.Open(seconds, settings.Raffle.JoinCommand);
};
raffle.ParticipantCountChanged += count =>
{
    Console.WriteLine($"[RAFFLE] participants={count}");
    overlay.SetParticipantCount(count);
};
raffle.Cancelled += () =>
{
    Console.WriteLine("[RAFFLE] cancelled; recruit keeps its game/default identity.");
    overlay.ShowCancelled();
    lock (raffleQueueGate) currentRecruitFollowerId = null;
    StartNextQueuedRaffle();
};
raffle.Completed += winner =>
{
    if (winner is null) overlay.ShowNoParticipants();
    else overlay.ShowWinner(winner.Nickname);
    _ = HandleRaffleWinnerAsync(winner);
};

async Task HandleRaffleWinnerAsync(RaffleEntry? winner)
{
    if (winner is null)
    {
        Console.WriteLine("[RAFFLE] no participants; recruit keeps its game/default identity.");
        currentRecruitFollowerId = null;
        StartNextQueuedRaffle();
        return;
    }

    if (!currentRecruitFollowerId.HasValue)
    {
        Console.WriteLine("[RAFFLE] winner selected, but no game recruit is bound to this raffle. No game data was changed.");
        return;
    }

    Console.WriteLine($"[RAFFLE] winner: {winner.Nickname} -> recruit {currentRecruitFollowerId.Value}");

    FollowerAppearanceSelection? selectedAppearance = null;
    if (cloud is not null && settings.Cloud.PreferRemoteViewerAppearance)
    {
        try
        {
            selectedAppearance = await cloud.GetViewerAppearanceAsync(streamerChannelId, winner.ViewerId, stop.Token);
            if (selectedAppearance is not null)
                Console.WriteLine($"[CLOUD] viewer appearance loaded: {selectedAppearance.FormId}");
        }
        catch (Exception ex) { Console.WriteLine($"[CLOUD] viewer appearance lookup failed: {ex.Message}"); }
    }

    selectedAppearance ??= appearances.Get(streamerChannelId, winner.ViewerId);

    await bridge.SendAsync(GameMessageTypes.ApplyRecruitIdentity, new ApplyRecruitIdentityCommand
    {
        RecruitFollowerId = currentRecruitFollowerId.Value,
        ViewerId = winner.ViewerId,
        Nickname = winner.Nickname,
        SaveId = currentSaveId,
        RaffleId = currentRaffleId,
        Appearance = selectedAppearance
    }, stop.Token);
}

void BeginRaffleFor(RaffleRequestedEvent request)
{
    lock (raffleQueueGate)
    {
        currentRecruitFollowerId = request.RecruitFollowerId;
        currentSaveId = request.SaveId;
        queuedRecruitIds.Remove(request.RecruitFollowerId);
    }
    Console.WriteLine($"[RAFFLE] game recruit detected: ID={request.RecruitFollowerId}, save={request.SaveId}");
    if (settings.Raffle.AutoStartOnGameRequest)
        _ = raffle.StartAsync(settings.Raffle.DurationSeconds, stop.Token);
}

async Task<bool> TryConnectCloudAsync(bool logFailure)
{
    if (developmentMode || string.IsNullOrWhiteSpace(cloudBaseUrl) || string.IsNullOrWhiteSpace(accessToken))
        return false;

    await cloudConnectGate.WaitAsync(stop.Token);
    try
    {
        if (cloud?.IsAuthenticated == true) return true;

        AppearanceApiClient? candidate = null;
        try
        {
            candidate = new AppearanceApiClient(cloudBaseUrl);
            await candidate.AuthenticateCompanionAsync(accessToken, stop.Token);
            var previous = cloud;
            cloud = candidate;
            candidate = null;
            previous?.Dispose();
            Console.WriteLine($"[CLOUD] connected: {cloud.BaseUrl}");
            if (!string.IsNullOrWhiteSpace(frontendUrl))
            {
                var publicFrontendUrl = frontendUrl.Trim().TrimEnd('/');
                Console.WriteLine($"[CLOUD] viewer setup URL: {publicFrontendUrl}/?streamer={Uri.EscapeDataString(streamerChannelId)}");
            }
            return true;
        }
        catch (Exception ex)
        {
            candidate?.Dispose();
            if (logFailure)
                Console.WriteLine($"[CLOUD] not ready yet; will retry: {ex.Message}");
            return false;
        }
    }
    finally
    {
        cloudConnectGate.Release();
    }
}

void StartNextQueuedRaffle()
{
    RaffleRequestedEvent? next = null;
    lock (raffleQueueGate)
    {
        if (currentRecruitFollowerId.HasValue || raffle.IsOpen || pendingRaffleRequests.Count == 0) return;
        next = pendingRaffleRequests.Dequeue();
    }
    BeginRaffleFor(next);
}


async Task UploadLatestCatalogAsync()
{
    if (!settings.Cloud.UploadCatalog) return;

    // Snapshot the catalog before the first await.  The game sends an empty/unknown
    // catalog while it is still on the splash/main-menu, followed by the real save
    // catalog.  Never publish the transient empty catalog and never let an older
    // fire-and-forget upload race with the valid one.
    var catalog = appearances.LatestCatalog;
    if (catalog is null || catalog.Forms.Count == 0 || string.IsNullOrWhiteSpace(catalog.SaveId) || catalog.SaveId == "unknown")
    {
        if (catalog is not null)
            Console.WriteLine($"[CLOUD] catalog upload skipped: save={catalog.SaveId}, forms={catalog.Forms.Count}");
        return;
    }

    var allowedFormIds = appearances.AllowedFormIds.ToArray();
    if (cloud?.IsAuthenticated != true && !await TryConnectCloudAsync(logFailure: false)) return;
    Exception? lastError = null;
    for (var attempt = 1; attempt <= 3 && !stop.IsCancellationRequested; attempt++)
    {
        try
        {
            await cloud!.UploadCatalogAsync(streamerChannelId, catalog, allowedFormIds, stop.Token);
            Console.WriteLine($"[CLOUD] catalog uploaded+verified: streamer={streamerChannelId}, save={catalog.SaveId}, forms={catalog.Forms.Count}, allowed={allowedFormIds.Length}");
            return;
        }
        catch (Exception ex)
        {
            lastError = ex;
            Console.WriteLine($"[CLOUD] catalog upload attempt {attempt}/3 failed: {ex.Message}");
            if (attempt < 3)
                await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
        }
    }
    if (lastError is not null)
        Console.WriteLine($"[CLOUD] catalog upload failed after retries: {lastError.Message}");
}

var bridgeTask = bridge.RunAsync(stop.Token);
Task? realtimeTask = null;
Task? cloudRetryTask = null;
Task? catalogRefreshTask = null;

if (settings.Appearance.AutoRefreshCatalog)
{
    var refreshSeconds = Math.Clamp(settings.Appearance.RefreshIntervalSeconds, 10, 3600);
    Console.WriteLine($"[APPEARANCE] automatic catalog refresh enabled: every {refreshSeconds}s while in game");
    catalogRefreshTask = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(refreshSeconds), stop.Token);
                var status = lastGameStatus;
                if (status?.InGame != true || string.IsNullOrWhiteSpace(status.SaveId) || status.SaveId == "unknown" || !bridge.IsGameConnected)
                    continue;

                if (Interlocked.Exchange(ref catalogRefreshInFlight, 1) != 0)
                    continue;

                try
                {
                    await bridge.SendAsync(GameMessageTypes.GetAppearanceCatalog, new AppearanceCatalogRequest
                    {
                        IncludeModded = settings.Appearance.IncludeModdedForms,
                        IncludeSpecial = settings.Appearance.IncludeSpecialForms
                    }, stop.Token);
                }
                finally
                {
                    // The bridge is request/event based rather than request/response based.
                    // A short guard prevents accidental bursts; the next interval is 30s by default.
                    Interlocked.Exchange(ref catalogRefreshInFlight, 0);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref catalogRefreshInFlight, 0);
                Console.WriteLine($"[APPEARANCE] automatic refresh warning: {ex.Message}");
            }
        }
    }, stop.Token);
}

var rosterRefreshTask = Task.Run(async () =>
{
    while (!stop.IsCancellationRequested)
    {
        try
        {
            if (bridge.IsGameConnected && lastGameStatus?.InGame == true && currentSaveId != "unknown")
                await bridge.SendAsync(GameMessageTypes.GetFollowerRoster, new { }, stop.Token);
            await Task.Delay(TimeSpan.FromSeconds(5), stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"[FOLLOWER-ROSTER] refresh warning: {ex.Message}");
            try { await Task.Delay(TimeSpan.FromSeconds(5), stop.Token); } catch { break; }
        }
    }
}, stop.Token);

if (!developmentMode && !string.IsNullOrWhiteSpace(cloudBaseUrl) && accessToken is not null)
{
    cloudRetryTask = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (cloud?.IsAuthenticated != true)
                    await TryConnectCloudAsync(logFailure: false);
                await Task.Delay(TimeSpan.FromSeconds(5), stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[CLOUD] retry loop warning: {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stop.Token); } catch { break; }
            }
        }
    }, stop.Token);
}

if (!developmentMode && api is not null && accessToken is not null)
{
    var realtime = new ChzzkRealtimeClient(api);

    realtime.Chat += chat =>
    {
        var nickname = string.IsNullOrWhiteSpace(chat.Profile?.Nickname) ? "(unknown)" : chat.Profile.Nickname;
        var content = NormalizeChatCommand(chat.Content);
        Console.WriteLine($"[CHAT] channel={chat.ChannelId} sender={chat.SenderChannelId} nickname={nickname} content=\"{content}\"");

        if (content.Equals(NormalizeChatCommand(settings.Raffle.JoinCommand), StringComparison.OrdinalIgnoreCase))
        {
            if (!raffle.IsOpen)
            {
                Console.WriteLine($"[RAFFLE] ignored join from {nickname}: raffle is not open");
                return;
            }
            if (settings.Raffle.PreventExistingFollowerInSameSave && currentSaveId != "unknown")
            {
                var record = followers.Find(streamerChannelId, chat.SenderChannelId, currentSaveId);
                if (record is not null)
                {
                    var rosterIsCurrent = string.Equals(latestRosterSaveId, currentSaveId, StringComparison.Ordinal)
                                          && latestRosterAt != DateTimeOffset.MinValue;
                    if (rosterIsCurrent)
                    {
                        if (!latestRosterFollowersById.TryGetValue(record.FollowerId, out var rosterEntry))
                        {
                            followers.Remove(streamerChannelId, chat.SenderChannelId, currentSaveId);
                            Console.WriteLine($"[FOLLOWER-RECONCILE] stale viewer mapping cleared on join: {nickname}, followerId={record.FollowerId}, save={currentSaveId}; follower ID no longer exists; re-entry allowed");
                        }
                        else if (!FollowerRosterNameMatchesViewer(rosterEntry.Name, record.LastKnownNickname))
                        {
                            // Follower IDs can be reused by the game. An ID-only match is therefore not
                            // sufficient evidence that this is still the same viewer-owned follower.
                            followers.Remove(streamerChannelId, chat.SenderChannelId, currentSaveId);
                            Console.WriteLine($"[FOLLOWER-RECONCILE] stale/reused follower ID cleared on join: viewer={nickname}, mappedFollowerId={record.FollowerId}, expectedName='{record.LastKnownNickname}', actualName='{rosterEntry.Name}', save={currentSaveId}; re-entry allowed");
                        }
                        else
                        {
                            Console.WriteLine($"[RAFFLE] rejected {nickname}: followerId={record.FollowerId} verified in loaded save, actualName='{rosterEntry.Name}' (rosterCurrent=True)");
                            return;
                        }
                    }
                    else
                    {
                        // If the roster is not current, keep the conservative behavior. A fresh roster
                        // request is already running every 5 seconds, so this state should be brief.
                        Console.WriteLine($"[RAFFLE] rejected {nickname}: follower mapping exists but game roster is not current yet (followerId={record.FollowerId}, save={currentSaveId})");
                        return;
                    }
                }
            }
            var joined = raffle.Join(chat.SenderChannelId, nickname);
            Console.WriteLine(joined
                ? $"[RAFFLE] joined: {nickname} ({chat.SenderChannelId}), participants={raffle.ParticipantCount}"
                : $"[RAFFLE] duplicate/invalid join ignored: {nickname} ({chat.SenderChannelId}), participants={raffle.ParticipantCount}");
            return;
        }

        // Development-only transport/game adapter test. Not intended as the normal viewer flow.
        if (content.Equals(settings.Raffle.DeveloperSpawnCommand, StringComparison.OrdinalIgnoreCase))
        {
            _ = bridge.SendAsync(GameMessageTypes.SpawnFollower,
                new SpawnFollowerCommand(chat.SenderChannelId, nickname, currentSaveId, "developer-test", appearances.Get(streamerChannelId, chat.SenderChannelId)), stop.Token);
        }
    };

    realtime.Donation += donation =>
    {
        var amount = donation.ParsedAmount;
        var area = lastGameStatus?.Area ?? "UNKNOWN";
        var decision = rules.ResolveDecision(amount, area);
        Console.WriteLine($"[DONATION][RECEIVED] nickname='{donation.DonatorNickname}', channel={donation.DonatorChannelId}, amount={amount:N0}, area={area}, text='{donation.DonationText ?? string.Empty}'");
        if (decision.Effect == "NONE")
        {
            Console.WriteLine($"[DONATION][RULE] amount={amount:N0} -> NONE ({decision.EventName})");
            return;
        }

        _ = SendDonationEffectAsync(
            donation.DonatorChannelId,
            donation.DonatorNickname,
            amount,
            donation.DonationText,
            decision);
    };

    realtime.Subscription += sub =>
        Console.WriteLine($"[SUBSCRIPTION] {sub.SubscriberNickname} tier={sub.TierNo} month={sub.Month}");

    realtimeTask = realtime.RunAsync(accessToken, stop.Token);
}

async Task SyncChzzkMarkersToGameAsync(string saveId, bool validateAgainstRoster)
{
    if (string.IsNullOrWhiteSpace(saveId) || saveId == "unknown" || !bridge.IsGameConnected) return;

    var markers = new List<ChzzkFollowerMarker>();
    var records = followers.GetForSave(streamerChannelId, saveId);
    var offlineFallback = false;

    // If CHZZK credentials are temporarily unavailable (for example an expired AWS SSO session),
    // LocalStreamerId is normally dev-local-streamer and would otherwise produce an empty marker
    // list that clears all village CHZZK badges. In offline/dev mode, recover persisted records
    // from this save across streamer IDs, but only keep entries whose follower ID + nickname match
    // the live game roster. This makes the fallback safe against stale/reused follower IDs.
    if (developmentMode && records.Count == 0 &&
        validateAgainstRoster && string.Equals(latestRosterSaveId, saveId, StringComparison.Ordinal))
    {
        records = followers.GetForSaveAnyStreamer(saveId);
        offlineFallback = records.Count > 0;
        if (offlineFallback)
            Console.WriteLine($"[FOLLOWER-NAMEPLATE] offline credential fallback: considering {records.Count} persisted record(s) across streamer IDs for save={saveId}; live roster identity validation is required.");
    }

    foreach (var record in records)
    {
        if (validateAgainstRoster && string.Equals(latestRosterSaveId, saveId, StringComparison.Ordinal))
        {
            if (!latestRosterFollowersById.TryGetValue(record.FollowerId, out var actual)) continue;
            if (!FollowerRosterNameMatchesViewer(actual.Name, record.LastKnownNickname)) continue;
        }

        markers.Add(new ChzzkFollowerMarker
        {
            FollowerId = record.FollowerId,
            ViewerId = record.ViewerChannelId,
            Nickname = NormalizeFollowerIdentityName(record.LastKnownNickname)
        });
    }

    if (developmentMode && offlineFallback && markers.Count == 0)
    {
        Console.WriteLine($"[FOLLOWER-NAMEPLATE] offline fallback found no roster-verified CHZZK followers; preserving current game markers instead of sending a destructive empty sync.");
        return;
    }

    var signature = saveId + "|" + string.Join(";", markers
        .OrderBy(x => x.FollowerId)
        .Select(x => $"{x.FollowerId}:{x.ViewerId}:{x.Nickname}"));
    if (string.Equals(signature, lastNameplateSyncSignature, StringComparison.Ordinal))
        return;

    await bridge.SendAsync(GameMessageTypes.SyncChzzkFollowerMarkers, new ChzzkFollowerMarkerSync
    {
        SaveId = saveId,
        Followers = markers
    }, stop.Token);
    lastNameplateSyncSignature = signature;
    Console.WriteLine($"[FOLLOWER-NAMEPLATE] synced CHZZK markers to game: save={saveId}, count={markers.Count}, ids=[{string.Join(",", markers.Select(x => x.FollowerId).OrderBy(x => x))}]");
}

static bool FollowerRosterNameMatchesViewer(string? actualName, string? expectedNickname)
{
    var actual = NormalizeFollowerIdentityName(actualName);
    var expected = NormalizeFollowerIdentityName(expectedNickname);
    if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected)) return false;
    return string.Equals(actual, expected, StringComparison.Ordinal);
}

static string NormalizeFollowerIdentityName(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return string.Empty;
    var normalized = value.Normalize(NormalizationForm.FormKC).Trim();

    // Backward compatibility with devbridge10g and earlier, where the CHZZK marker
    // was persisted inside FollowerInfo.Name as TMP rich text.
    const string legacyPrefix = "<color=#00C471>Chzzk</color> ";
    if (normalized.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
        normalized = normalized.Substring(legacyPrefix.Length).Trim();
    else if (normalized.StartsWith("Chzzk ", StringComparison.OrdinalIgnoreCase))
        normalized = normalized.Substring("Chzzk ".Length).Trim();

    return normalized;
}

static string NormalizeChatCommand(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return string.Empty;
    // Normalize compatibility characters and remove invisible formatting characters
    // that can be introduced by mobile keyboards / copy-paste.
    var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
    return normalized
        .Replace("\u200B", string.Empty)
        .Replace("\u200C", string.Empty)
        .Replace("\u200D", string.Empty)
        .Replace("\uFEFF", string.Empty);
}

async Task ConsoleLoopAsync()
{
    PrintCommands();
    while (!stop.IsCancellationRequested)
    {
        var line = await Console.In.ReadLineAsync();
        if (line is null) break;
        var rawCommand = line.Trim();
        if (rawCommand.Length == 0) continue;
        var normalized = rawCommand.ToLowerInvariant();

        if (normalized.StartsWith("form allow "))
        {
            var id = rawCommand.Substring("form allow ".Length).Trim();
            appearances.SetFormAllowed(id, true);
            Console.WriteLine($"[APPEARANCE] allowed {id}");
            _ = UploadLatestCatalogAsync();
            continue;
        }
        if (normalized.StartsWith("form deny "))
        {
            var id = rawCommand.Substring("form deny ".Length).Trim();
            appearances.SetFormAllowed(id, false);
            Console.WriteLine($"[APPEARANCE] denied {id}");
            _ = UploadLatestCatalogAsync();
            continue;
        }
        if (normalized.StartsWith("dev spawn "))
        {
            var nickname = rawCommand.Substring("dev spawn ".Length).Trim();
            if (string.IsNullOrWhiteSpace(nickname))
            {
                Console.WriteLine("Usage: dev spawn <nickname>");
                continue;
            }

            var viewerId = "dev-" + Slug(nickname);
            Console.WriteLine($"[DEV] spawning follower nickname={nickname}, viewer={viewerId}");
            await bridge.SendAsync(GameMessageTypes.SpawnFollower,
                new SpawnFollowerCommand(viewerId, nickname, currentSaveId, "developer-console", appearances.Get(streamerChannelId, viewerId)), stop.Token);
            continue;
        }
        if (normalized.StartsWith("dev join "))
        {
            var nickname = rawCommand.Substring("dev join ".Length).Trim();
            if (string.IsNullOrWhiteSpace(nickname))
            {
                Console.WriteLine("Usage: dev join <nickname>");
                continue;
            }
            var viewerId = "dev-" + Slug(nickname);
            var joined = raffle.Join(viewerId, nickname);
            Console.WriteLine(joined ? $"[DEV] raffle joined: {nickname}" : $"[DEV] raffle join rejected: {nickname}");
            continue;
        }
        if (normalized.StartsWith("dev donation "))
        {
            var amountText = rawCommand.Substring("dev donation ".Length).Trim().Replace(",", string.Empty);
            if (!long.TryParse(amountText, out var amount) || amount < 0)
            {
                Console.WriteLine("Usage: dev donation <amount>");
                continue;
            }
            var area = lastGameStatus?.Area ?? "UNKNOWN";
            var decision = rules.ResolveDecision(amount, area);
            Console.WriteLine($"[DEV DONATION] {amount:N0} area={area} -> {decision.EventName} ({decision.Effect})");
            if (decision.Effect != "NONE")
                await SendDonationEffectAsync("dev-donor", "DEV 후원자", amount, "development test", decision);
            continue;
        }

        switch (normalized)
        {
            case "raffle start":
                _ = raffle.StartAsync(settings.Raffle.DurationSeconds, stop.Token);
                break;
            case "raffle cancel": raffle.Cancel(); break;
            case "raffle draw": raffle.Complete(); break;
            case "forms":
                foreach (var form in appearances.LatestCatalog?.Forms ?? new())
                    Console.WriteLine($"  {(appearances.AllowedFormIds.Contains(form.FormId) ? "[x]" : "[ ]")} {form.DisplayName} ({form.FormId}) unlocked={form.IsUnlocked} special={form.IsSpecial} modded={form.IsModded}");
                break;
            case "refresh-forms":
                await bridge.SendAsync(GameMessageTypes.GetAppearanceCatalog, new AppearanceCatalogRequest
                {
                    IncludeModded = settings.Appearance.IncludeModdedForms,
                    IncludeSpecial = settings.Appearance.IncludeSpecialForms
                }, stop.Token);
                break;
            case "status":
                Console.WriteLine($"MODE={(developmentMode ? "DEV" : "CHZZK")}, CHZZK={(developmentMode ? "disabled" : streamerChannelName)}, GAME={bridge.IsGameConnected}, SAVE={currentSaveId}, RECRUIT={currentRecruitFollowerId?.ToString() ?? "none"}, RAFFLE={raffle.IsOpen}, participants={raffle.ParticipantCount}, queue={pendingRaffleRequests.Count}, CLOUD={(cloud?.IsAuthenticated == true ? "connected" : "off")}, CATALOG={latestCatalogCount}, CATALOG_SAVE={latestCatalogSaveId}");
                break;
            case "help":
                PrintCommands();
                break;
            case "exit":
            case "quit":
                stop.Cancel();
                break;
            default:
                Console.WriteLine("Unknown command. Type 'help'.");
                break;
        }
    }
}

var tasks = new List<Task> { bridgeTask, ConsoleLoopAsync() };
if (realtimeTask is not null) tasks.Add(realtimeTask);
if (catalogRefreshTask is not null) tasks.Add(catalogRefreshTask);
tasks.Add(rosterRefreshTask);
if (cloudRetryTask is not null) tasks.Add(cloudRetryTask);

try
{
    await Task.WhenAll(tasks);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    // normal shutdown
}
finally
{
    cloud?.Dispose();
    http?.Dispose();
}

async Task SendDonationEffectAsync(string viewerId, string nickname, long amount, string? message, DonationDecision decision)
{
    var requestId = Guid.NewGuid().ToString("N");
    Console.WriteLine($"[DONATION][RULE] request={ShortId(requestId)} amount={amount:N0} tier={decision.TierName} -> event='{decision.EventName}' effect={decision.Effect}");

    if (!bridge.IsGameConnected)
    {
        Console.WriteLine($"[DONATION][SEND] request={ShortId(requestId)} skipped: game mod is not connected.");
        return;
    }

    var command = new DonationEffectCommand(
        viewerId,
        nickname,
        amount,
        decision.Effect,
        message,
        decision.EventName,
        requestId);

    Console.WriteLine($"[DONATION][SEND] request={ShortId(requestId)} -> game bridge, event='{decision.EventName}', effect={decision.Effect}");
    await bridge.SendAsync(GameMessageTypes.DonationEffect, command, stop.Token);
}

static string ShortId(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "-" : value!.Substring(0, Math.Min(8, value.Length));

void PrintCommands()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  status | help | exit");
    Console.WriteLine("  raffle start | raffle cancel | raffle draw");
    Console.WriteLine("  forms | form allow <id> | form deny <id> | refresh-forms");
    Console.WriteLine("  dev spawn <nickname>       (개발 전용: 새 신도 생성 API 테스트, 실제 방송 흐름에서는 사용 안 함)");
    Console.WriteLine("  dev join <nickname>        (CHZZK 없이 추첨 참가 테스트)");
    Console.WriteLine("  dev donation <amount>      (CHZZK 없이 후원 효과 테스트)");
}

static string BuildCatalogFingerprint(FollowerAppearanceCatalog catalog)
{
    // Stable, order-independent representation. Include all viewer-relevant fields so
    // unlocking a form, adding variants/colors, or changing metadata triggers upload.
    var parts = catalog.Forms
        .OrderBy(x => x.FormId, StringComparer.Ordinal)
        .Select(x => string.Join("|",
            x.FormId,
            x.DisplayName,
            x.IsUnlocked ? "1" : "0",
            x.IsSpecial ? "1" : "0",
            x.IsModded ? "1" : "0",
            string.Join(",", x.VariantIds.OrderBy(v => v, StringComparer.Ordinal)),
            string.Join(",", x.ColorIds.OrderBy(c => c, StringComparer.Ordinal)),
            string.Join(",", x.ColorHexById.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value))));
    return catalog.SaveId + "\n" + string.Join("\n", parts);
}

static bool IsTruthy(string? value) =>
    value is not null && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                          || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                          || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                          || value.Equals("on", StringComparison.OrdinalIgnoreCase));

static string Slug(string value)
{
    var chars = value.Where(char.IsLetterOrDigit).Take(32).ToArray();
    return chars.Length == 0 ? Guid.NewGuid().ToString("N") : new string(chars).ToLowerInvariant();
}
