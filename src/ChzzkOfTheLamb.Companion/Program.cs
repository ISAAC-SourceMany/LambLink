using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Collections.Concurrent;
using ChzzkOfTheLamb.Companion.Appearance;
using ChzzkOfTheLamb.Companion.Chzzk;
using ChzzkOfTheLamb.Companion.Cloud;
using ChzzkOfTheLamb.Companion.Configuration;
using ChzzkOfTheLamb.Companion.Diagnostics;
using ChzzkOfTheLamb.Companion.GameBridge;
using ChzzkOfTheLamb.Companion.Raffle;
using ChzzkOfTheLamb.Companion.Overlay;
using ChzzkOfTheLamb.Companion.Rules;
using ChzzkOfTheLamb.Companion.Storage;
using ChzzkOfTheLamb.Companion.ViewerPage;
using ChzzkOfTheLamb.Protocol;

const string ReleaseVersion = "1.0.0-rc35";
const string ProductionApiBase = "https://y0eblkdmu5.execute-api.ap-northeast-2.amazonaws.com";
const string ProductionFrontendUrl = "https://d1gvw9ccym1qvn.cloudfront.net";

#if RELEASE_DISTRIBUTION
bool IsReleaseDistribution = true;
#else
bool IsReleaseDistribution = false;
#endif

// Release builds stay pinned to production unless a developer explicitly opts into
// staging.  Staging also gets its own data directory so OAuth/session, appearance,
// donation outbox, and diagnostics never overwrite the live Companion state.
var stagingMode = IsReleaseDistribution
                  && IsTruthy(Environment.GetEnvironmentVariable("COTL_STAGING_MODE"));
var defaultDataDirectoryName = stagingMode ? "ChzzkOfTheLamb-Staging" : "ChzzkOfTheLamb";
var stagingDataDirOverride = stagingMode
    ? Environment.GetEnvironmentVariable("COTL_STAGING_DATA_DIR")
    : null;
var dataDir = string.IsNullOrWhiteSpace(stagingDataDirOverride)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), defaultDataDirectoryName)
    : Path.GetFullPath(Environment.ExpandEnvironmentVariables(stagingDataDirOverride.Trim()));
Directory.CreateDirectory(dataDir);
var diagnosticLogPath = Path.Combine(dataDir, "companion-rc35.log");
var originalConsoleOut = Console.Out;
var originalConsoleError = Console.Error;
using var diagnosticLogWriter = new RollingFileTextWriter(
    diagnosticLogPath,
    maxBytes: 5L * 1024 * 1024,
    archiveCount: 4);
Console.SetOut(TextWriter.Synchronized(new TeeTextWriter(originalConsoleOut, diagnosticLogWriter)));
Console.SetError(TextWriter.Synchronized(new TeeTextWriter(originalConsoleError, diagnosticLogWriter)));
var supportBundles = new SupportBundleService(dataDir, ReleaseVersion, diagnosticLogPath);
AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
    Console.Error.WriteLine($"[DIAG][UNHANDLED] terminating={eventArgs.IsTerminating}, exception={eventArgs.ExceptionObject}");
TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    Console.Error.WriteLine($"[DIAG][UNOBSERVED-TASK] {eventArgs.Exception}");
    eventArgs.SetObserved();
};
Console.WriteLine($"[DIAG][SESSION-BEGIN] version={ReleaseVersion}, utc={DateTimeOffset.UtcNow:O}, pid={Environment.ProcessId}, log={diagnosticLogPath}, retention=5MiB+4archives, stdout=true, stderr=true");

ChzzkCredentials? chzzkCredentials = null;
#if !RELEASE_DISTRIBUTION
try
{
    chzzkCredentials = ChzzkCredentialProvider.Load();
}
catch (Exception ex)
{
    Console.WriteLine($"[CONFIG] CHZZK credential provider failed: {ex.Message}");
}
#endif

var clientId = chzzkCredentials?.ClientId;
var clientSecret = chzzkCredentials?.ClientSecret;
var redirectUri = Environment.GetEnvironmentVariable("CHZZK_REDIRECT_URI")
                  ?? "http://127.0.0.1:17881/callback/";
var forceDevelopmentMode = !IsReleaseDistribution && IsTruthy(Environment.GetEnvironmentVariable("CHZZK_DEV_MODE"));

var settings = CompanionSettings.LoadOrCreate(Path.Combine(dataDir, "settings.json"));
var followers = new ViewerFollowerRepository(Path.Combine(dataDir, "viewer-followers.json"));
var appearances = new AppearanceStore(Path.Combine(dataDir, "viewer-appearances.json"));
var viewerPage = new ViewerPageShare(
    dataDir,
    stagingMode ? "CHZZK 시청자 외형 설정 페이지 (Staging).url" : null);
var raffle = new RaffleManager();
var rules = new DonationRuleEngine(settings.Donation);

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

await using var overlay = new RaffleOverlayServer();
overlay.Start();

var hasChzzkCredentials = !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
var developmentMode = !IsReleaseDistribution && (forceDevelopmentMode || !hasChzzkCredentials);

Console.WriteLine($"CHZZK Companion for Cult of the Lamb - v{ReleaseVersion}");
if (IsReleaseDistribution)
{
    Console.WriteLine(stagingMode
        ? "[MODE] RELEASE / CHZZK LIVE / STAGING"
        : "[MODE] RELEASE / CHZZK LIVE");
    Console.WriteLine("[CONFIG] AWS CLI/SSO: not used by distribution build");
#if RC_TEST_TOOLS
    Console.WriteLine("[TEST TOOLS] RC35_TEST_TOOLS enabled: dev donation command is available; do not distribute this Companion.");
#endif
}
else
{
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
        Console.WriteLine("[MODE] CHZZK LIVE (developer credentials)");
    }
}

var configuredWebApiBase = IsReleaseDistribution
    ? stagingMode
        ? RequireHttpsEnvironmentVariable("COTL_WEB_API_BASE")
        : ProductionApiBase
    : Environment.GetEnvironmentVariable("COTL_WEB_API_BASE");
if (!string.IsNullOrWhiteSpace(configuredWebApiBase))
    Console.WriteLine($"[WEB] API configured: {configuredWebApiBase}");
if (developmentMode && !string.IsNullOrWhiteSpace(configuredWebApiBase))
    Console.WriteLine("[WEB] My Lamb API is configured, but Companion authentication/catalog upload requires CHZZK LIVE mode.");

var streamerChannelId = settings.Development.LocalStreamerId;
var streamerChannelName = settings.Development.LocalStreamerName;
string? accessToken = null;
string? refreshToken = null;
DateTimeOffset accessTokenExpiresAt = DateTimeOffset.MinValue;
ChzzkApiClient? api = null;
HttpClient? http = null;
AppearanceApiClient? cloud = null;
var cloudConnectGate = new SemaphoreSlim(1, 1);
var cloudBaseUrl = configuredWebApiBase;
if (string.IsNullOrWhiteSpace(cloudBaseUrl) && settings.Cloud.Enabled)
    cloudBaseUrl = settings.Cloud.ApiBaseUrl;
var frontendUrl = IsReleaseDistribution
    ? stagingMode
        ? RequireHttpsEnvironmentVariable("COTL_WEB_FRONTEND_URL")
        : ProductionFrontendUrl
    : Environment.GetEnvironmentVariable("COTL_WEB_FRONTEND_URL");
if (string.IsNullOrWhiteSpace(frontendUrl)) frontendUrl = settings.Cloud.FrontendUrl;

if (!developmentMode)
{
    http = new HttpClient();

    if (IsReleaseDistribution)
    {
        if (string.IsNullOrWhiteSpace(cloudBaseUrl))
            throw new InvalidOperationException("Release build is missing the production Auth Gateway URL.");

        api = new ChzzkApiClient(http);
        Console.WriteLine("[AUTH] CHZZK 로그인을 시작합니다...");
        var auth = await ProductionOAuth.AuthorizeAsync(http, cloudBaseUrl!, redirectUri, stop.Token);
        accessToken = auth.AccessToken;
        refreshToken = auth.RefreshToken;
        accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, auth.ExpiresIn));
        streamerChannelId = auth.StreamerChannelId;
        streamerChannelName = auth.StreamerChannelName;
    }
    else
    {
        api = new ChzzkApiClient(http, clientId!, clientSecret!);
        Console.WriteLine("Opening browser for CHZZK OAuth...");
        var (code, state) = await LoopbackOAuth.AuthorizeAsync(api, redirectUri, stop.Token);
        var tokens = await api.ExchangeCodeAsync(code, state, stop.Token);
        var me = await api.GetMeAsync(tokens.AccessToken, stop.Token);
        accessToken = tokens.AccessToken;
        refreshToken = tokens.RefreshToken;
        accessTokenExpiresAt = tokens.AcquiredAt.AddSeconds(Math.Max(60, tokens.ExpiresIn));
        streamerChannelId = me.ChannelId;
        streamerChannelName = me.ChannelName;
        Console.WriteLine($"[CHZZK] connected: {streamerChannelName} ({streamerChannelId})");
    }

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
var reservationRecoveryInFlight = 0;
DateTimeOffset latestRosterAt = DateTimeOffset.MinValue;
string? lastRosterFingerprint = null;
long bridgeDispatchSequence = 0;
long gameConnectionGeneration = 0;
long catalogSyncGeneration = 0;
long lastRuntimePumpStatusUnixMs = 0;
var gameSyncPhase = "DISCONNECTED";
var donationTraces = new DonationTraceRegistry();
var presentedDonationResults = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
var donationDeliveries = new DonationDeliveryRepository(Path.Combine(
    dataDir,
    $"donation-outbox-{DiagnosticPrivacy.StableFileKey(streamerChannelId)}.json"));
DonationRuntimeStateEvent lastDonationRuntimeState = new()
{
    IsReady = false,
    TimersPaused = true,
    Reason = "STARTING",
    Area = "UNKNOWN"
};
long donationEventSequence = 0;
string? lastSupportBundlePath = null;

await using var bridge = new GameBridgeServer();
bridge.ConnectionChanged += connected =>
{
    var generation = Interlocked.Increment(ref gameConnectionGeneration);
    Console.WriteLine(connected ? "[GAME] connected" : "[GAME] disconnected");
    if (connected)
    {
        currentSaveId = "unknown";
        lastGameStatus = null;
        latestCatalogCount = 0;
        latestCatalogSaveId = "unknown";
        Interlocked.Exchange(ref lastRuntimePumpStatusUnixMs, 0);
        gameSyncPhase = "SOCKET_CONNECTED";
        lastDonationRuntimeState = new DonationRuntimeStateEvent
        {
            IsReady = false,
            TimersPaused = true,
            Reason = "WAITING_FOR_MOD_STATE",
            Area = "UNKNOWN"
        };
        overlay.SetDonationRuntimeState(true, "WAITING_FOR_MOD_STATE");
        _ = MaintainInitialGameStateSyncAsync(generation);
    }
    else
    {
        var abandoned = donationTraces.AbandonAll();
        gameSyncPhase = "DISCONNECTED";
        lastDonationRuntimeState = new DonationRuntimeStateEvent
        {
            IsReady = false,
            TimersPaused = true,
            Reason = "GAME_DISCONNECTED",
            Area = "UNKNOWN"
        };
        overlay.SetDonationRuntimeState(true, "GAME_DISCONNECTED");
        if (abandoned > 0)
            Console.WriteLine($"[DONATION][OUTBOX][RETRY-ARMED] abandonedTraces={abandoned}, durablePending={donationDeliveries.PendingCount}");
    }
};

string lastNameplateSyncSignature = string.Empty;

async Task MaintainInitialGameStateSyncAsync(long generation)
{
    var attempt = 0;
    while (!stop.IsCancellationRequested
           && bridge.IsGameConnected
           && Volatile.Read(ref gameConnectionGeneration) == generation
           && lastGameStatus?.RuntimePumpActive != true)
    {
        attempt++;
        try
        {
            gameSyncPhase = "WAITING_FOR_RUNTIME_PUMP";
            Console.WriteLine($"[BRIDGE][STATE][TX] GET_GAME_STATUS attempt={attempt}, generation={generation}");
            var sent = await bridge.SendAsync(GameMessageTypes.GetGameStatus, new { }, stop.Token);
            Console.WriteLine(sent
                ? $"[BRIDGE][STATE][TX-OK] GET_GAME_STATUS delivered; awaiting pump-active GAME_STATUS, attempt={attempt}"
                : $"[BRIDGE][STATE][TX-FAILED] GET_GAME_STATUS attempt={attempt}; retrying");
            await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"[BRIDGE][STATE][TX-FAILED] GET_GAME_STATUS attempt={attempt}: {ex}");
            try { await Task.Delay(TimeSpan.FromSeconds(2), stop.Token); } catch { break; }
        }
    }

    if (lastGameStatus?.RuntimePumpActive == true)
        Console.WriteLine($"[BRIDGE][STATE][SYNC] runtime pump confirmed after {attempt} request(s), updates={lastGameStatus.RuntimeUpdateCount}");
}

void StartCatalogSync(string saveId)
{
    var syncGeneration = Interlocked.Increment(ref catalogSyncGeneration);
    var connectionGeneration = Volatile.Read(ref gameConnectionGeneration);
    _ = MaintainCatalogSyncAsync(syncGeneration, connectionGeneration, saveId);
}

async Task MaintainCatalogSyncAsync(long syncGeneration, long connectionGeneration, string saveId)
{
    var attempt = 0;
    while (!stop.IsCancellationRequested
           && bridge.IsGameConnected
           && Volatile.Read(ref catalogSyncGeneration) == syncGeneration
           && Volatile.Read(ref gameConnectionGeneration) == connectionGeneration
           && string.Equals(currentSaveId, saveId, StringComparison.Ordinal)
           && (!string.Equals(latestCatalogSaveId, saveId, StringComparison.Ordinal) || latestCatalogCount <= 0))
    {
        attempt++;
        try
        {
            gameSyncPhase = "WAITING_FOR_CATALOG";
            Console.WriteLine($"[BRIDGE][SYNC] requesting appearance catalog and follower roster for save={saveId}, attempt={attempt}");
            await bridge.SendAsync(GameMessageTypes.GetAppearanceCatalog, new AppearanceCatalogRequest
            {
                IncludeModded = settings.Appearance.IncludeModdedForms,
                IncludeSpecial = settings.Appearance.IncludeSpecialForms
            }, stop.Token);
            await bridge.SendAsync(GameMessageTypes.GetFollowerRoster, new { }, stop.Token);
            await Task.Delay(TimeSpan.FromSeconds(5), stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"[BRIDGE][SYNC][FAILED] save={saveId}, attempt={attempt}: {ex}");
            try { await Task.Delay(TimeSpan.FromSeconds(5), stop.Token); } catch { break; }
        }
    }
}

bridge.MessageReceived += envelope =>
{
    var dispatchId = Interlocked.Increment(ref bridgeDispatchSequence);
    var dispatchStarted = Stopwatch.GetTimestamp();
    Console.WriteLine($"[BRIDGE][DISPATCH][BEGIN] id={dispatchId}, type={envelope.Type}, thread={Environment.CurrentManagedThreadId}");
    try
    {
        switch (envelope.Type)
        {
            case GameMessageTypes.GameStatus:
            {
                var status = JsonSerializer.Deserialize<GameStatusEvent>(envelope.PayloadJson)!;
                if (!status.RuntimePumpActive && lastGameStatus?.RuntimePumpActive == true)
                {
                    Console.WriteLine($"[BRIDGE][STATE][RX-IGNORED] cache fallback arrived after pump-active status; save={status.SaveId}, updates={status.RuntimeUpdateCount}");
                    break;
                }
                var previousSave = currentSaveId;
                currentSaveId = status.SaveId;
                if (status.RuntimePumpActive)
                    Interlocked.Exchange(ref lastRuntimePumpStatusUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Console.WriteLine($"[BRIDGE][STATE][RX] GAME_STATUS pump={status.RuntimePumpActive}, updates={status.RuntimeUpdateCount}, inGame={status.InGame}, save={status.SaveId}, area={status.Area}, mod={status.ModVersion}");
                if (status.DonationStateRevision >= lastDonationRuntimeState.Revision)
                {
                    lastDonationRuntimeState = new DonationRuntimeStateEvent
                    {
                        IsReady = status.DonationReady,
                        TimersPaused = status.DonationTimersPaused,
                        Reason = status.DonationPauseReason,
                        Area = status.Area,
                        PendingDonations = status.PendingDonationCount,
                        Revision = status.DonationStateRevision
                    };
                    overlay.SetDonationRuntimeState(status.DonationTimersPaused, status.DonationPauseReason);
                }
                else
                {
                    Console.WriteLine($"[DONATION][GATE][STATUS-IGNORED] staleRevision={status.DonationStateRevision}, currentRevision={lastDonationRuntimeState.Revision}");
                }

                var changed = lastGameStatus is null
                              || lastGameStatus.RuntimePumpActive != status.RuntimePumpActive
                              || lastGameStatus.InGame != status.InGame
                              || !string.Equals(lastGameStatus.SaveId, status.SaveId, StringComparison.Ordinal)
                              || !string.Equals(lastGameStatus.ModVersion, status.ModVersion, StringComparison.Ordinal)
                              || !string.Equals(lastGameStatus.GameVersion, status.GameVersion, StringComparison.Ordinal)
                              || !string.Equals(lastGameStatus.Area, status.Area, StringComparison.Ordinal);

                if (changed)
                    Console.WriteLine($"[GAME] inGame={status.InGame} save={status.SaveId} area={status.Area} mod={status.ModVersion} game={status.GameVersion}");

                // Refresh the catalog automatically when a save becomes available or changes.
                if (status.RuntimePumpActive && status.InGame && status.SaveId != "unknown"
                    && (!string.Equals(previousSave, status.SaveId, StringComparison.Ordinal)
                        || lastGameStatus?.RuntimePumpActive != true
                        || lastGameStatus?.InGame != true))
                {
                    gameSyncPhase = "SAVE_READY";
                    StartCatalogSync(status.SaveId);
                }
                else if (status.RuntimePumpActive)
                {
                    if (!status.InGame) gameSyncPhase = "MAIN_MENU";
                    else if (status.SaveId == "unknown") gameSyncPhase = "WAITING_FOR_SAVE";
                    else if (string.Equals(latestCatalogSaveId, status.SaveId, StringComparison.Ordinal) && latestCatalogCount > 0)
                        gameSyncPhase = "READY";
                    else gameSyncPhase = "WAITING_FOR_CATALOG";
                }

                lastGameStatus = status;
                break;
            }
            case GameMessageTypes.AppearanceCatalog:
            {
                var catalog = JsonSerializer.Deserialize<FollowerAppearanceCatalog>(envelope.PayloadJson)!;
                var newlyAllowedForms = appearances.UpdateCatalog(catalog);
                latestCatalogCount = catalog.Forms.Count;
                latestCatalogSaveId = catalog.SaveId;
                if (newlyAllowedForms.Count > 0)
                    Console.WriteLine($"[APPEARANCE][AUTO-ALLOW] newly unlocked forms enabled for viewers: {string.Join(", ", newlyAllowedForms)}");
                // Catalog messages are emitted from the active save. Use them as a safe
                // synchronization source if the periodic GAME_STATUS has not caught up yet.
                if (!string.IsNullOrWhiteSpace(catalog.SaveId) && catalog.SaveId != "unknown")
                    currentSaveId = catalog.SaveId;
                if (catalog.Forms.Count > 0
                    && string.Equals(catalog.SaveId, currentSaveId, StringComparison.Ordinal))
                    gameSyncPhase = "READY";
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
                        // The vanilla alive roster intentionally omits dead, imprisoned and
                        // expedition followers. Absence is not proof of death and must never
                        // delete generation history or reopen creation rights.
                        if (!latestRosterFollowersById.TryGetValue(record.FollowerId, out var actual)) continue;

                        if (!FollowerRosterNameMatchesViewer(actual.Name, record.FollowerName))
                        {
                            // Follower IDs are reusable and an unsaved raffle result legitimately
                            // disappears after the game is closed. ID-only ownership must never
                            // rename another follower or resurrect an unsaved result.
                            stale.Add(record);
                            Console.WriteLine($"[FOLLOWER-RECONCILE] stale/reused ID detected from roster: viewer={record.LastKnownNickname} ({record.ViewerChannelId}), followerId={record.FollowerId}, actualName='{actual.Name}', save={roster.SaveId}");
                        }
                        else if (actual.IsDead && record.IsAlive)
                        {
                            followers.ApplyLifecycle(streamerChannelId, $"roster-died:{roster.SaveId}:{record.FollowerId}:{roster.GeneratedAt.ToUnixTimeSeconds()}", roster.SaveId, record.FollowerId, "Died", roster.GeneratedAt, actual.DeathReason ?? "Unknown");
                            _ = SyncViewerFollowerStateAsync(record.ViewerChannelId, roster.SaveId);
                        }
                        else if (!actual.IsDead && !record.IsAlive && record.DiedAt.HasValue)
                        {
                            followers.ApplyLifecycle(streamerChannelId, $"roster-resurrected:{roster.SaveId}:{record.FollowerId}:{roster.GeneratedAt.ToUnixTimeSeconds()}", roster.SaveId, record.FollowerId, "Resurrected", roster.GeneratedAt, null);
                            _ = SyncViewerFollowerStateAsync(record.ViewerChannelId, roster.SaveId);
                        }
                    }
                    foreach (var record in stale)
                        Console.WriteLine($"[FOLLOWER-RECONCILE] identity mismatch retained for manual recovery: viewer={record.LastKnownNickname} ({record.ViewerChannelId}), followerId={record.FollowerId}, save={roster.SaveId}");
                }

                var rosterFingerprint = roster.SaveId + ":" + string.Join(",", latestRosterFollowerIds.OrderBy(x => x));
                if (stale.Count > 0 || !string.Equals(lastRosterFingerprint, rosterFingerprint, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[FOLLOWER-ROSTER] save={roster.SaveId}, actual={latestRosterFollowerIds.Count}, staleRemoved={stale.Count}, ids=[{string.Join(",", latestRosterFollowerIds.OrderBy(x => x))}]");
                    lastRosterFingerprint = rosterFingerprint;
                    foreach (var viewerId in followers.GetForSave(streamerChannelId, roster.SaveId).Select(x => x.ViewerChannelId).Distinct(StringComparer.Ordinal))
                        _ = SyncViewerFollowerStateAsync(viewerId, roster.SaveId);
                }
                _ = SyncChzzkMarkersToGameAsync(roster.SaveId, validateAgainstRoster: true);
                _ = RecoverPendingReservationsAsync(roster);
                break;
            }
            case GameMessageTypes.FollowerLifecycle:
            {
                var lifecycle = JsonSerializer.Deserialize<FollowerLifecycleEvent>(envelope.PayloadJson)!;
                var record = followers.GetForSave(streamerChannelId, lifecycle.SaveId).FirstOrDefault(x => x.FollowerId == lifecycle.FollowerId);
                if (record is not null && followers.ApplyLifecycle(streamerChannelId, lifecycle.EventId, lifecycle.SaveId, lifecycle.FollowerId, lifecycle.EventType, lifecycle.OccurredAt, lifecycle.Cause))
                {
                    Console.WriteLine($"[FOLLOWER-LIFECYCLE] {lifecycle.EventType}: follower={record.FollowerName}, cause={lifecycle.Cause ?? "<none>"}");
                    _ = SyncViewerFollowerStateAsync(record.ViewerChannelId, lifecycle.SaveId);
                }
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
                    _ = SyncViewerFollowerStateAsync(result.ViewerId, result.SaveId);
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
                        FollowerName = string.IsNullOrWhiteSpace(result.FollowerName) ? result.Nickname : result.FollowerName,
                        SaveId = result.SaveId,
                        FollowerId = result.RecruitFollowerId,
                        Generation = result.Generation,
                        Appearance = result.AppliedAppearance
                    });
                    if (string.Equals(latestRosterSaveId, result.SaveId, StringComparison.Ordinal)) latestRosterFollowerIds.Add(result.RecruitFollowerId);
                    Console.WriteLine($"[RAFFLE] identity applied: {result.Nickname} -> recruit {result.RecruitFollowerId}");
                    _ = SyncChzzkMarkersToGameAsync(result.SaveId, validateAgainstRoster: false);
                    _ = SyncViewerFollowerStateAsync(result.ViewerId, result.SaveId);
                    _ = UpdateReservationStatusSafeAsync(result.ViewerId, result.SaveId, result.Generation, result.RaffleId, "Created", result.AppliedAppearance);
                }
                else
                {
                    Console.WriteLine($"[RAFFLE] identity apply failed: {result.Error}");
                    _ = UpdateReservationStatusSafeAsync(result.ViewerId, result.SaveId, result.Generation, result.RaffleId, "Draft", null);
                }

                lock (raffleQueueGate) currentRecruitFollowerId = null;
                _ = NotifyRaffleRoundClosedAsync(
                    result.RecruitFollowerId,
                    result.Success ? "identity-applied" : "identity-apply-failed",
                    allowRetry: !result.Success);
                StartNextQueuedRaffle();
                break;
            }
            case GameMessageTypes.DonationRuntimeState:
            {
                var state = JsonSerializer.Deserialize<DonationRuntimeStateEvent>(envelope.PayloadJson)!;
                if (state.Revision > 0 && state.Revision < lastDonationRuntimeState.Revision)
                {
                    Console.WriteLine($"[DONATION][GATE][RX-IGNORED] staleRevision={state.Revision}, currentRevision={lastDonationRuntimeState.Revision}");
                    break;
                }

                lastDonationRuntimeState = state;
                overlay.SetDonationRuntimeState(state.TimersPaused, state.Reason);
                Console.WriteLine($"[DONATION][GATE][RX] revision={state.Revision}, ready={state.IsReady}, timersPaused={state.TimersPaused}, reason={state.Reason}, area={state.Area}, pending={state.PendingDonations}, evidence={state.Evidence}");
                break;
            }
            case GameMessageTypes.DonationEffectResult:
            {
                var result = JsonSerializer.Deserialize<DonationEffectResult>(envelope.PayloadJson)!;
                var matched = donationTraces.TryComplete(result.RequestId, out var trace, out var elapsedMs);
                var outboxMatched = result.Success || !result.Retryable
                    ? donationDeliveries.TryComplete(result.RequestId, out _)
                    : donationDeliveries.Contains(result.RequestId);
                var accepted = matched || outboxMatched;
                var correlation = matched
                    ? $"matched=true, elapsedMs={elapsedMs:F1}, source={trace!.Source}, area={trace.Area}"
                    : $"matched=false, elapsedMs=unknown, outboxMatched={outboxMatched}";
                if (result.Success)
                {
                    Console.WriteLine($"[DONATION][ACK][SUCCESS] request={ShortId(result.RequestId)}, {correlation}, event='{result.EventName}', effect={result.Effect}; details={result.Details}");
                    if (accepted && presentedDonationResults.TryAdd(result.RequestId, 0))
                    {
                        overlay.ShowDonation(result.Nickname, result.Amount, result.EventName, seconds: 5);
                        overlay.RegisterDonationBuff(result.Effect, result.EventName);
                    }
                    else
                    {
                        Console.WriteLine($"[DONATION][ACK][DUPLICATE-IGNORED] request={ShortId(result.RequestId)}; terminal result was already consumed");
                    }
                }
                else
                {
                    Console.WriteLine($"[DONATION][ACK][FAILED] request={ShortId(result.RequestId)}, {correlation}, retryable={result.Retryable}, event='{result.EventName}', effect={result.Effect}; error={result.Error}");
                }
                break;
            }
            case GameMessageTypes.RaffleRequested:
            {
                var request = JsonSerializer.Deserialize<RaffleRequestedEvent>(envelope.PayloadJson)!;
                var accepted = request.RecruitFollowerId > 0;
                var ackStatus = accepted ? "received" : "invalid-recruit-id";
                var startNow = false;
                if (accepted)
                {
                    lock (raffleQueueGate)
                    {
                        if (currentRecruitFollowerId == request.RecruitFollowerId || queuedRecruitIds.Contains(request.RecruitFollowerId))
                            ackStatus = currentRecruitFollowerId == request.RecruitFollowerId ? "already-active" : "already-queued";
                        else if (currentRecruitFollowerId.HasValue || raffle.IsOpen)
                        {
                            pendingRaffleRequests.Enqueue(request);
                            queuedRecruitIds.Add(request.RecruitFollowerId);
                            ackStatus = "queued";
                            Console.WriteLine($"[RAFFLE] queued game recruit {request.RecruitFollowerId}; queue={pendingRaffleRequests.Count}");
                        }
                        else
                        {
                            startNow = true;
                            ackStatus = "started";
                        }
                    }
                }
                if (startNow) BeginRaffleFor(request);
                _ = bridge.SendAsync(GameMessageTypes.RaffleRequestAck, new RaffleRequestAck
                {
                    RecruitFollowerId = request.RecruitFollowerId,
                    Accepted = accepted,
                    Status = ackStatus
                }, stop.Token);
                Console.WriteLine($"[RAFFLE][ACK-TX] recruit={request.RecruitFollowerId}, accepted={accepted}, status={ackStatus}");
                break;
            }
        }
    }
    catch (Exception ex) { Console.WriteLine($"[BRIDGE][DISPATCH][FAILED] id={dispatchId}, type={envelope.Type}: {ex}"); }
    finally
    {
        var elapsedMs = (Stopwatch.GetTimestamp() - dispatchStarted) * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"[BRIDGE][DISPATCH][END] id={dispatchId}, type={envelope.Type}, elapsedMs={elapsedMs:F1}");
    }
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
    int? cancelledRecruitId;
    lock (raffleQueueGate)
    {
        cancelledRecruitId = currentRecruitFollowerId;
        currentRecruitFollowerId = null;
    }
    if (cancelledRecruitId.HasValue)
        _ = NotifyRaffleRoundClosedAsync(cancelledRecruitId.Value, "cancelled", allowRetry: true);
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
        int? emptyRecruitId;
        lock (raffleQueueGate)
        {
            emptyRecruitId = currentRecruitFollowerId;
            currentRecruitFollowerId = null;
        }
        if (emptyRecruitId.HasValue)
            await NotifyRaffleRoundClosedAsync(emptyRecruitId.Value, "no-participants", allowRetry: true);
        StartNextQueuedRaffle();
        return;
    }

    int recruitFollowerId;
    string raffleSaveId;
    string? raffleId;
    lock (raffleQueueGate)
    {
        if (!currentRecruitFollowerId.HasValue)
        {
            Console.WriteLine("[RAFFLE] winner selected, but no game recruit is bound to this raffle. No game data was changed.");
            return;
        }
        recruitFollowerId = currentRecruitFollowerId.Value;
        raffleSaveId = currentSaveId;
        raffleId = currentRaffleId;
    }

    if (!followers.CanCreate(streamerChannelId, winner.ViewerId, raffleSaveId))
    {
        Console.WriteLine($"[RAFFLE] winner rejected: {winner.Nickname} already has a living follower in save={raffleSaveId}.");
        await CompleteRaffleWithoutIdentityAsync(recruitFollowerId, "winner-ineligible");
        return;
    }

    var generation = followers.NextGeneration(streamerChannelId, winner.ViewerId, raffleSaveId);
    var followerName = ViewerFollowerRepository.BuildFollowerName(winner.Nickname, generation);
    Console.WriteLine($"[RAFFLE] winner: {followerName} -> recruit {recruitFollowerId}");

    FollowerAppearanceSelection? selectedAppearance = null;
    var requireAuthoritativeCloudAppearance = IsReleaseDistribution || settings.Cloud.PreferRemoteViewerAppearance;
    if (requireAuthoritativeCloudAppearance)
    {
        try
        {
            if (cloud?.IsAuthenticated != true && !await TryConnectCloudAsync(logFailure: false))
                throw new InvalidOperationException("authoritative appearance service is unavailable");
            selectedAppearance = await cloud!.FinalizeViewerAppearanceAsync(streamerChannelId, winner.ViewerId, winner.Nickname, raffleSaveId, generation, raffleId, recruitFollowerId, followerName, stop.Token);
            if (selectedAppearance is not null)
                Console.WriteLine($"[CLOUD] viewer appearance loaded: {selectedAppearance.FormId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLOUD] authoritative appearance finalization failed; raffle assignment aborted: {ex.Message}");
            await CompleteRaffleWithoutIdentityAsync(recruitFollowerId, "appearance-finalization-failed");
            return;
        }
    }

    // Production uses the server value captured after the winner is known. A stale local
    // cache must not override the viewer's final saved selection.
    if (!requireAuthoritativeCloudAppearance)
        selectedAppearance = appearances.Get(streamerChannelId, winner.ViewerId);
    if (selectedAppearance is not null && !appearances.Validate(selectedAppearance))
    {
        Console.WriteLine($"[APPEARANCE] saved viewer selection is unavailable in the current save; using the recruit's game/default appearance instead: {selectedAppearance.FormId}");
        selectedAppearance = null;
    }

    var identitySent = await bridge.SendAsync(GameMessageTypes.ApplyRecruitIdentity, new ApplyRecruitIdentityCommand
    {
        RecruitFollowerId = recruitFollowerId,
        ViewerId = winner.ViewerId,
        Nickname = winner.Nickname,
        FollowerName = followerName,
        Generation = generation,
        SaveId = raffleSaveId,
        RaffleId = raffleId,
        Appearance = selectedAppearance
    }, stop.Token);
    if (identitySent)
        await UpdateReservationStatusSafeAsync(winner.ViewerId, raffleSaveId, generation, raffleId, "AppliedUnconfirmed", null);
    else if (!identitySent)
        await CompleteRaffleWithoutIdentityAsync(recruitFollowerId, "identity-dispatch-failed");
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

async Task NotifyRaffleRoundClosedAsync(int recruitFollowerId, string status, bool allowRetry)
{
    if (recruitFollowerId <= 0) return;

    var message = new RaffleRoundClosed
    {
        RecruitFollowerId = recruitFollowerId,
        Status = status,
        AllowRetry = allowRetry
    };
    for (var attempt = 1; attempt <= 3 && !stop.IsCancellationRequested; attempt++)
    {
        var sent = await bridge.SendAsync(GameMessageTypes.RaffleRoundClosed, message, stop.Token);
        Console.WriteLine($"[RAFFLE][ROUND-CLOSED-TX] recruit={recruitFollowerId}, status={status}, allowRetry={allowRetry}, attempt={attempt}/3, sent={sent}");
        if (sent) return;
        if (attempt < 3) await Task.Delay(250 * attempt, stop.Token);
    }
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
                var fallbackViewerUrl = $"{publicFrontendUrl}/?streamer={Uri.EscapeDataString(streamerChannelId)}";
                Console.WriteLine($"[CLOUD] viewer setup URL: {fallbackViewerUrl}");
                try
                {
                    viewerPage.Configure(publicFrontendUrl, streamerChannelId);
                    viewerPage.PrintBanner(Console.Out);
                    foreach (var warning in viewerPage.LastWarnings)
                        Console.WriteLine($"[VIEWER PAGE][WARNING] {warning}");
                }
                catch (Exception ex)
                {
                    // Sharing helpers are convenience features. A shortcut/file-system failure
                    // must never invalidate the authenticated cloud session or catalog upload.
                    Console.WriteLine($"[VIEWER PAGE][WARNING] 공유 도구 초기화 실패: {ex.Message}");
                    Console.WriteLine($"[VIEWER PAGE] 공유 주소: {fallbackViewerUrl}");
                }
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

    // Persisted allow/deny policy is shared across save slots, but the cloud payload must
    // expose only forms that the currently active save reports as unlocked. This prevents a
    // form unlocked in one slot from leaking into another slot's viewer catalog.
    var unlockedFormIds = catalog.Forms
        .Where(x => x.IsUnlocked)
        .Select(x => x.FormId)
        .ToHashSet(StringComparer.Ordinal);
    var allowedFormIds = appearances.AllowedFormIds
        .Where(unlockedFormIds.Contains)
        .ToArray();
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
var donationDeliveryTask = Task.Run(MaintainDonationOutboxAsync, stop.Token);
Task? realtimeTask = null;
Task? cloudRetryTask = null;
Task? catalogRefreshTask = null;
Task? tokenMaintenanceTask = null;

if (!developmentMode && api is not null && http is not null && !string.IsNullOrWhiteSpace(refreshToken))
{
    tokenMaintenanceTask = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(30), stop.Token);
                if (DateTimeOffset.UtcNow >= accessTokenExpiresAt - TimeSpan.FromMinutes(90))
                {
                    ProductionOAuthResult refreshed;
                    if (IsReleaseDistribution)
                        refreshed = await ProductionOAuth.RefreshAsync(http, cloudBaseUrl!, refreshToken!, stop.Token);
                    else
                    {
                        var tokenSet = await api.RefreshTokenAsync(refreshToken!, stop.Token);
                        refreshed = new ProductionOAuthResult(tokenSet.AccessToken, tokenSet.RefreshToken, tokenSet.TokenType, tokenSet.ExpiresIn, streamerChannelId, streamerChannelName);
                    }
                    accessToken = refreshed.AccessToken;
                    refreshToken = refreshed.RefreshToken;
                    accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, refreshed.ExpiresIn));
                    Console.WriteLine($"[AUTH] CHZZK access token refreshed; next expiry={accessTokenExpiresAt:O}");
                }
                if (cloud is not null && accessToken is not null)
                    await cloud.AuthenticateCompanionAsync(accessToken, stop.Token);
                else
                    await TryConnectCloudAsync(logFailure: false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) { Console.WriteLine($"[AUTH] token/session maintenance failed; will retry: {ex.Message}"); }
        }
    }, stop.Token);
}

async Task CompleteRaffleWithoutIdentityAsync(int recruitFollowerId, string status)
{
    lock (raffleQueueGate)
    {
        if (currentRecruitFollowerId == recruitFollowerId) currentRecruitFollowerId = null;
    }
    await NotifyRaffleRoundClosedAsync(recruitFollowerId, status, allowRetry: true);
    StartNextQueuedRaffle();
}

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
                if (record is not null && !followers.CanCreate(streamerChannelId, chat.SenderChannelId, currentSaveId))
                {
                    var rosterIsCurrent = string.Equals(latestRosterSaveId, currentSaveId, StringComparison.Ordinal)
                                          && latestRosterAt != DateTimeOffset.MinValue;
                    if (rosterIsCurrent)
                    {
                        if (!latestRosterFollowersById.TryGetValue(record.FollowerId, out var rosterEntry))
                        {
                            Console.WriteLine($"[RAFFLE] rejected {nickname}: follower state is unresolved (not in current roster), followerId={record.FollowerId}");
                            return;
                        }
                        if (!FollowerRosterNameMatchesViewer(rosterEntry.Name, record.FollowerName))
                        {
                            Console.WriteLine($"[FOLLOWER-RECONCILE] follower identity mismatch; preserving mapping and rejecting until lifecycle is known: viewer={nickname}, mappedFollowerId={record.FollowerId}");
                            return;
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
        var eventSequence = Interlocked.Increment(ref donationEventSequence);
        var requestId = Guid.NewGuid().ToString("N");
        try
        {
            if (!string.Equals(donation.ChannelId, streamerChannelId, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"[DONATION][TERMINAL][CHANNEL-MISMATCH] seq={eventSequence}, request={ShortId(requestId)}, expectedHash={DiagnosticPrivacy.ShortHash(streamerChannelId)}, actualHash={DiagnosticPrivacy.ShortHash(donation.ChannelId)}");
                return;
            }
            if (!donation.TryGetAmount(out var amount, out var amountError))
            {
                Console.Error.WriteLine($"[DONATION][TERMINAL][INVALID-AMOUNT] seq={eventSequence}, request={ShortId(requestId)}, reason={amountError}");
                return;
            }
            var area = lastGameStatus?.Area ?? "UNKNOWN";
            var decision = rules.ResolveDecision(amount, area);
            var donorHash = DiagnosticPrivacy.ShortHash(donation.DonatorChannelId);
            Console.WriteLine($"[DONATION][RX] seq={eventSequence}, request={ShortId(requestId)}, source=CHZZK, donationType={donation.DonationType}, donorHash={donorHash}, amount={amount:N0}, area={area}, messageChars={donation.DonationText?.Length ?? 0}");
            if (decision.Effect == "NONE")
            {
                Console.WriteLine($"[DONATION][TERMINAL][NO-EFFECT] request={ShortId(requestId)}, amount={amount:N0}, area={area}, rule={decision.EventName}");
                return;
            }

            var delivery = new DonationDeliveryRecord
            {
                RequestId = requestId,
                Source = "CHZZK",
                DonorHash = donorHash,
                ViewerId = donation.DonatorChannelId ?? string.Empty,
                Nickname = string.IsNullOrWhiteSpace(donation.DonatorNickname) ? "후원자" : donation.DonatorNickname,
                Amount = amount,
                Area = area,
                TierName = decision.TierName,
                Effect = decision.Effect,
                EventName = decision.EventName,
                ReceivedAtUtc = DateTimeOffset.UtcNow
            };
            if (!donationDeliveries.TryAdd(delivery))
                throw new InvalidOperationException("Duplicate donation request ID was generated.");
            Console.WriteLine($"[DONATION][OUTBOX][ENQUEUED] request={ShortId(requestId)}, durablePending={donationDeliveries.PendingCount}; donor message is not persisted");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DONATION][TERMINAL][HANDLER-EXCEPTION] seq={eventSequence}, request={ShortId(requestId)}, type={ex.GetType().FullName}, error={ex}");
        }
    };

    realtime.Subscription += sub =>
        Console.WriteLine($"[SUBSCRIPTION] {sub.SubscriberNickname} tier={sub.TierNo} month={sub.Month}");

    realtimeTask = realtime.RunAsync(() => accessToken ?? throw new InvalidOperationException("CHZZK access token is unavailable."), stop.Token);
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
            if (!FollowerRosterNameMatchesViewer(actual.Name, record.FollowerName)) continue;
        }

        markers.Add(new ChzzkFollowerMarker
        {
            FollowerId = record.FollowerId,
            ViewerId = record.ViewerChannelId,
            Nickname = NormalizeFollowerIdentityName(record.FollowerName)
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

async Task SyncViewerFollowerStateAsync(string viewerId, string saveId)
{
    if (cloud is null || !cloud.IsAuthenticated || string.IsNullOrWhiteSpace(viewerId) || saveId == "unknown") return;
    try
    {
        var snapshot = followers.GetStateSnapshot(streamerChannelId, viewerId, saveId);
        var history = snapshot.History
            .Select(x => new
            {
                generation = x.Generation, followerId = x.FollowerId, followerName = x.FollowerName,
                viewerNickname = x.LastKnownNickname, isAlive = x.IsAlive, createdAt = x.CreatedAt,
                diedAt = x.DiedAt, deathReason = x.DeathReason, resurrectedAt = x.ResurrectedAt,
                appearance = x.Appearance,
                events = x.Events.Select(e => new { type = e.Type, at = e.At, cause = e.Cause }).ToArray()
            }).ToArray();
        await cloud.PutViewerStateAsync(streamerChannelId, viewerId, new
        {
            saveId,
            revision = snapshot.Revision,
            canCreate = snapshot.CanCreate,
            nextGeneration = snapshot.NextGeneration,
            history
        }, stop.Token);
    }
    catch (Exception ex) { Console.WriteLine($"[CLOUD] viewer state sync failed: viewer={viewerId}, error={ex.Message}"); }
}

async Task UpdateReservationStatusSafeAsync(string viewerId, string saveId, int generation, string? raffleId, string status, FollowerAppearanceSelection? appearance)
{
    if (cloud is null || !cloud.IsAuthenticated) return;
    try { await cloud.UpdateReservationStatusAsync(streamerChannelId, viewerId, saveId, generation, raffleId, status, appearance, stop.Token); }
    catch (Exception ex) { Console.WriteLine($"[FOLLOWER-RESERVATION] status update failed: viewer={viewerId}, generation={generation}, status={status}, error={ex.Message}"); }
}

async Task RecoverPendingReservationsAsync(FollowerRosterSnapshot roster)
{
    if (cloud is null || !cloud.IsAuthenticated || roster.SaveId == "unknown" || Interlocked.Exchange(ref reservationRecoveryInFlight, 1) != 0) return;
    try
    {
        var pending = await cloud.GetPendingReservationsAsync(streamerChannelId, roster.SaveId, stop.Token);
        foreach (var reservation in pending)
        {
            var actual = roster.Followers.FirstOrDefault(x => x.FollowerId == reservation.RecruitFollowerId);
            if (actual is null || !FollowerRosterNameMatchesViewer(actual.Name, reservation.FollowerName))
            {
                var lastStatusAt = Math.Max(reservation.StatusUpdatedAt, reservation.ReservedAt);
                if (lastStatusAt > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastStatusAt >= 600)
                {
                    await UpdateReservationStatusSafeAsync(reservation.ViewerChannelId, roster.SaveId, reservation.Generation, reservation.RaffleId, "Draft", null);
                    Console.WriteLine($"[FOLLOWER-RECOVERY] released stale reservation after 10m: viewer={reservation.ViewerChannelId}, follower={reservation.FollowerName}, id={reservation.RecruitFollowerId}");
                }
                continue;
            }
            var recoveredAppearance = actual.Appearance is null ? reservation.AppliedAppearance ?? reservation.Appearance : new FollowerAppearanceSelection
            {
                FormId = reservation.Appearance?.FormId ?? actual.Appearance.FormId,
                VariantId = actual.Appearance.VariantId,
                ColorId = actual.Appearance.ColorId
            };
            var recoveredCreatedAt = reservation.ReservedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(reservation.ReservedAt) : roster.GeneratedAt;
            var existingRecord = followers.GetForSave(streamerChannelId, roster.SaveId).FirstOrDefault(x => x.FollowerId == reservation.RecruitFollowerId);
            if (existingRecord is not null)
            {
                existingRecord.Appearance = recoveredAppearance;
                followers.Upsert(existingRecord);
                await UpdateReservationStatusSafeAsync(reservation.ViewerChannelId, roster.SaveId, reservation.Generation, reservation.RaffleId, "Created", recoveredAppearance);
                await SyncViewerFollowerStateAsync(reservation.ViewerChannelId, roster.SaveId);
                continue;
            }
            followers.Upsert(new ViewerFollowerRecord
            {
                StreamerChannelId = streamerChannelId, ViewerChannelId = reservation.ViewerChannelId,
                LastKnownNickname = reservation.ViewerNickname, FollowerName = reservation.FollowerName,
                SaveId = roster.SaveId, FollowerId = reservation.RecruitFollowerId,
                Generation = reservation.Generation, IsAlive = !actual.IsDead,
                CreatedAt = recoveredCreatedAt,
                DiedAt = actual.IsDead ? roster.GeneratedAt : null,
                DeathReason = actual.IsDead ? actual.DeathReason ?? "Unknown" : null,
                Appearance = recoveredAppearance,
                Events = actual.IsDead
                    ? new List<ViewerFollowerHistoryEvent>
                    {
                        new() { Type = "Created", At = recoveredCreatedAt },
                        new() { Type = "Died", At = roster.GeneratedAt, Cause = actual.DeathReason ?? "Unknown" }
                    }
                    : new List<ViewerFollowerHistoryEvent> { new() { Type = "Created", At = recoveredCreatedAt } }
            });
            await UpdateReservationStatusSafeAsync(reservation.ViewerChannelId, roster.SaveId, reservation.Generation, reservation.RaffleId, "Created", recoveredAppearance);
            await SyncViewerFollowerStateAsync(reservation.ViewerChannelId, roster.SaveId);
            Console.WriteLine($"[FOLLOWER-RECOVERY] recovered unconfirmed generation: viewer={reservation.ViewerChannelId}, follower={reservation.FollowerName}, id={reservation.RecruitFollowerId}");
        }
    }
    catch (Exception ex) { Console.WriteLine($"[FOLLOWER-RECOVERY] pending reservation scan failed: {ex.Message}"); }
    finally { Interlocked.Exchange(ref reservationRecoveryInFlight, 0); }
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
#if RC_TEST_TOOLS
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
                await SendDonationEffectSafeAsync(
                    Guid.NewGuid().ToString("N"),
                    "DEV-CONSOLE",
                    DiagnosticPrivacy.ShortHash("dev-donor"),
                    "dev-donor",
                    "DEV 후원자",
                    amount,
                    "development test",
                    area,
                    decision,
                    stop.Token);
            continue;
        }
#endif

        switch (normalized)
        {
            case "overlay":
            case "overlay url":
                Console.WriteLine($"[OVERLAY] OBS browser source: {overlay.OverlayUrl}");
                Console.WriteLine($"[OVERLAY] clientDocument={overlay.ClientDocumentVersion}, current={overlay.IsClientDocumentCurrent}");
                break;
            case "viewer":
            case "viewer url":
                viewerPage.PrintBanner(Console.Out);
                break;
            case "viewer copy":
                viewerPage.TryCopyToClipboard(out var copyMessage);
                Console.WriteLine($"[VIEWER PAGE] {copyMessage}");
                break;
            case "viewer open":
                viewerPage.TryOpen(out var openMessage);
                Console.WriteLine($"[VIEWER PAGE] {openMessage}");
                break;
            case "support":
            case "support bundle":
            {
                try
                {
                    Console.WriteLine("[SUPPORT] 개인정보 제거 지원 로그 묶음을 로컬에서 생성합니다. 자동 업로드하지 않습니다...");
                    var lastPumpUnixMs = Interlocked.Read(ref lastRuntimePumpStatusUnixMs);
                    var pumpAgeSeconds = lastPumpUnixMs <= 0
                        ? -1
                        : (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastPumpUnixMs) / 1000.0;
                    var gameReady = bridge.IsGameConnected
                                    && lastGameStatus?.RuntimePumpActive == true
                                    && pumpAgeSeconds is >= 0 and <= 15
                                    && lastGameStatus?.InGame == true
                                    && currentSaveId != "unknown";
                    var snapshot = new SupportBundleSnapshot(
                        DateTimeOffset.UtcNow,
                        bridge.IsGameConnected,
                        gameReady,
                        gameSyncPhase,
                        currentSaveId,
                        !developmentMode,
                        cloud?.IsAuthenticated == true,
                        latestCatalogCount,
                        latestCatalogSaveId,
                        raffle.IsOpen,
                        raffle.ParticipantCount,
                        donationDeliveries.PendingCount);
                    var bundle = await Task.Run(() => supportBundles.Create(snapshot), stop.Token);
                    lastSupportBundlePath = bundle.ZipPath;
                    Console.WriteLine($"[SUPPORT][READY] report={bundle.ReportId}, bytes={bundle.Bytes}, file={bundle.ZipPath}");
                    Console.WriteLine("[SUPPORT] 압축 내부를 확인한 뒤 개발자에게 전달하세요.");
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SUPPORT][FAILED] type={ex.GetType().FullName}, error={ex}");
                }
                break;
            }
            case "support open":
            {
                var path = lastSupportBundlePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    Console.WriteLine("[SUPPORT] 먼저 'support' 명령으로 로그 묶음을 생성하세요.");
                    break;
                }
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                break;
            }
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
            {
                var lastPumpUnixMs = Interlocked.Read(ref lastRuntimePumpStatusUnixMs);
                var pumpAgeSeconds = lastPumpUnixMs <= 0
                    ? -1
                    : (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastPumpUnixMs) / 1000.0;
                var gameReady = bridge.IsGameConnected
                                && lastGameStatus?.RuntimePumpActive == true
                                && pumpAgeSeconds >= 0 && pumpAgeSeconds <= 15
                                && lastGameStatus?.InGame == true
                                && currentSaveId != "unknown";
                var displayedSyncPhase = bridge.IsGameConnected && pumpAgeSeconds > 15
                    ? "PUMP_STALE"
                    : gameSyncPhase;
                var overlayPollAge = overlay.StatePollAgeSeconds;
                Console.WriteLine($"MODE={(developmentMode ? "DEV" : "CHZZK")}, CHZZK={(developmentMode ? "disabled" : streamerChannelName)}, GAME={gameReady}, GAME_SOCKET={bridge.IsGameConnected}, GAME_READY={gameReady}, SYNC={displayedSyncPhase}, PUMP_AGE={(pumpAgeSeconds < 0 ? "none" : pumpAgeSeconds.ToString("F1") + "s")}, SAVE={currentSaveId}, AREA={lastDonationRuntimeState.Area}, DONATION_GATE={lastDonationRuntimeState.Reason}, DONATION_READY={lastDonationRuntimeState.IsReady}, DONATION_QUEUE={lastDonationRuntimeState.PendingDonations}, RECRUIT={currentRecruitFollowerId?.ToString() ?? "none"}, RAFFLE={raffle.IsOpen}, participants={raffle.ParticipantCount}, queue={pendingRaffleRequests.Count}, OVERLAY={(overlay.IsClientPolling ? "ready" : "not-polling")}, OVERLAY_DOC={overlay.ClientDocumentVersion}, OVERLAY_DOC_CURRENT={overlay.IsClientDocumentCurrent}, OVERLAY_POLL_AGE={(overlayPollAge < 0 ? "none" : overlayPollAge.ToString("F1") + "s")}, CLOUD={(cloud?.IsAuthenticated == true ? "connected" : "off")}, CATALOG={latestCatalogCount}, CATALOG_SAVE={latestCatalogSaveId}, DONATION_PENDING={donationTraces.PendingCount}, DONATION_OUTBOX={donationDeliveries.PendingCount}");
                Console.WriteLine($"VIEWER_PAGE={viewerPage.Url ?? "not-ready"}");
                break;
            }
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

var tasks = new List<Task> { bridgeTask, donationDeliveryTask, ConsoleLoopAsync() };
if (realtimeTask is not null) tasks.Add(realtimeTask);
if (catalogRefreshTask is not null) tasks.Add(catalogRefreshTask);
tasks.Add(rosterRefreshTask);
if (cloudRetryTask is not null) tasks.Add(cloudRetryTask);
if (tokenMaintenanceTask is not null) tasks.Add(tokenMaintenanceTask);

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
    Console.WriteLine($"[DIAG][SESSION-END] version={ReleaseVersion}, utc={DateTimeOffset.UtcNow:O}, pendingDonationTraces={donationTraces.PendingCount}, durableDonationOutbox={donationDeliveries.PendingCount}, cancellationRequested={stop.IsCancellationRequested}");
    cloud?.Dispose();
    http?.Dispose();
}

async Task MaintainDonationOutboxAsync()
{
    if (donationDeliveries.PendingCount > 0)
        Console.WriteLine($"[DONATION][OUTBOX][RECOVERED] durablePending={donationDeliveries.PendingCount}; delivery resumes when the game bridge is available");

    while (!stop.IsCancellationRequested)
    {
        try
        {
            if (!bridge.IsGameConnected)
            {
                await Task.Delay(500, stop.Token);
                continue;
            }
            // Keep bounded backpressure in front of the Mod's 256-item queue. This also keeps
            // acknowledgement monitoring and reconnect recovery lightweight during donation bursts.
            if (donationTraces.PendingCount >= 32)
            {
                await Task.Delay(100, stop.Token);
                continue;
            }

            var retryBefore = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2);
            var delivery = donationDeliveries.Snapshot().FirstOrDefault(x =>
                !donationTraces.Contains(x.RequestId) &&
                (!x.LastAttemptAtUtc.HasValue || x.LastAttemptAtUtc.Value <= retryBefore));
            if (delivery is null)
            {
                await Task.Delay(250, stop.Token);
                continue;
            }

            if (!donationDeliveries.TryRecordAttempt(delivery.RequestId, DateTimeOffset.UtcNow))
                continue;
            Console.WriteLine($"[DONATION][OUTBOX][DISPATCH] request={ShortId(delivery.RequestId)}, attempt={delivery.AttemptCount + 1}, durablePending={donationDeliveries.PendingCount}");
            await SendDonationEffectSafeAsync(
                delivery.RequestId,
                delivery.Source,
                delivery.DonorHash,
                delivery.ViewerId,
                delivery.Nickname,
                delivery.Amount,
                message: null,
                delivery.Area,
                new DonationDecision(delivery.Effect, delivery.EventName, delivery.TierName),
                stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DONATION][OUTBOX][WORKER-ERROR] type={ex.GetType().FullName}, error={ex.Message}");
            try { await Task.Delay(1000, stop.Token); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        }
    }
}

async Task SendDonationEffectSafeAsync(
    string requestId,
    string source,
    string donorHash,
    string viewerId,
    string nickname,
    long amount,
    string? message,
    string area,
    DonationDecision decision,
    CancellationToken cancellationToken)
{
    try
    {
        Console.WriteLine($"[DONATION][RULE] request={ShortId(requestId)}, source={source}, donorHash={donorHash}, amount={amount:N0}, area={area}, tier={decision.TierName}, event='{decision.EventName}', effect={decision.Effect}");

        if (!bridge.IsGameConnected)
        {
            Console.WriteLine($"[DONATION][TERMINAL][NOT-SENT] request={ShortId(requestId)}, reason=game-socket-disconnected, sync={gameSyncPhase}");
            return;
        }

        donationTraces.Begin(requestId, source, donorHash, amount, area, decision.TierName, decision.Effect, decision.EventName);
        var command = new DonationEffectCommand(
            viewerId,
            nickname,
            amount,
            decision.Effect,
            message,
            decision.EventName,
            requestId);

        Console.WriteLine($"[DONATION][TX][BEGIN] request={ShortId(requestId)}, event='{decision.EventName}', effect={decision.Effect}, messageChars={message?.Length ?? 0}");
        var sent = await bridge.SendAsync(GameMessageTypes.DonationEffect, command, cancellationToken);
        if (!sent)
        {
            donationTraces.TryAbandon(requestId, out _, out var elapsedMs);
            Console.WriteLine($"[DONATION][TERMINAL][TX-FAILED] request={ShortId(requestId)}, elapsedMs={elapsedMs:F1}, reason=bridge-send-returned-false");
            return;
        }

        Console.WriteLine($"[DONATION][TX][SENT] request={ShortId(requestId)}, awaiting=DONATION_EFFECT_RESULT, activeTimeoutSeconds=15, pausedWhileModGateBlocked=true");
        _ = MonitorDonationAcknowledgementAsync(requestId, cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        donationTraces.TryAbandon(requestId, out _, out _);
    }
    catch (Exception ex)
    {
        donationTraces.TryAbandon(requestId, out _, out var elapsedMs);
        Console.Error.WriteLine($"[DONATION][TERMINAL][EXCEPTION] request={ShortId(requestId)}, elapsedMs={elapsedMs:F1}, type={ex.GetType().FullName}, error={ex}");
    }
}

async Task MonitorDonationAcknowledgementAsync(string requestId, CancellationToken cancellationToken)
{
    try
    {
        const double activeTimeoutSeconds = 15;
        const double absoluteSafetyTimeoutSeconds = 1800;
        var started = Stopwatch.GetTimestamp();
        var previous = started;
        var activeWaitSeconds = 0d;
        string? lastPauseReason = null;

        while (donationTraces.Contains(requestId))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            var now = Stopwatch.GetTimestamp();
            var deltaSeconds = Math.Max(0d, (now - previous) / (double)Stopwatch.Frequency);
            previous = now;

            var runtimeState = lastDonationRuntimeState;
            if (!runtimeState.TimersPaused && runtimeState.IsReady)
            {
                activeWaitSeconds += deltaSeconds;
                lastPauseReason = null;
            }
            else if (!string.Equals(lastPauseReason, runtimeState.Reason, StringComparison.Ordinal))
            {
                lastPauseReason = runtimeState.Reason;
                Console.WriteLine($"[DONATION][ACK-WAIT][PAUSED] request={ShortId(requestId)}, reason={runtimeState.Reason}, area={runtimeState.Area}, pending={runtimeState.PendingDonations}, activeWaitSeconds={activeWaitSeconds:F1}");
            }

            var wallSeconds = (now - started) / (double)Stopwatch.Frequency;
            if (activeWaitSeconds < activeTimeoutSeconds && wallSeconds < absoluteSafetyTimeoutSeconds) continue;

            if (donationTraces.TryAbandon(requestId, out var trace, out var elapsedMs))
            {
                var timeoutKind = wallSeconds >= absoluteSafetyTimeoutSeconds ? "absolute-safety" : "active-gameplay";
                Console.Error.WriteLine($"[DONATION][TERMINAL][ACK-TIMEOUT] request={ShortId(requestId)}, kind={timeoutKind}, elapsedMs={elapsedMs:F1}, activeWaitSeconds={activeWaitSeconds:F1}, source={trace!.Source}, area={trace.Area}, effect={trace.Effect}, gameSocket={bridge.IsGameConnected}, sync={gameSyncPhase}, gate={runtimeState.Reason}");
            }
            break;
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // Normal application shutdown.
    }
}

static string ShortId(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "-" : value!.Substring(0, Math.Min(8, value.Length));

void PrintCommands()
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  status | help | exit");
    Console.WriteLine("  overlay | overlay url       (OBS 브라우저 소스 URL/문서 버전 확인)");
    Console.WriteLine("  viewer | viewer copy | viewer open");
    Console.WriteLine("  support | support open       (개인정보 제거 로그 ZIP 생성/열기)");
    Console.WriteLine("  raffle start | raffle cancel | raffle draw");
    Console.WriteLine("  forms | form allow <id> | form deny <id> | refresh-forms");
#if RC_TEST_TOOLS
    Console.WriteLine("  dev spawn <nickname>       (개발 전용)");
    Console.WriteLine("  dev join <nickname>        (개발 전용)");
    Console.WriteLine("  dev donation <amount>      (개발 전용)");
#endif
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

static string RequireHttpsEnvironmentVariable(string name)
{
    var value = Environment.GetEnvironmentVariable(name)?.Trim();
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"Staging mode requires {name}.");
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
        || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(uri.UserInfo))
        throw new InvalidOperationException($"Staging mode requires {name} to be an absolute HTTPS URL without embedded credentials.");
    return value.TrimEnd('/');
}

static bool IsTruthy(string? value) =>
    value is not null && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                          || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                          || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                          || value.Equals("on", StringComparison.OrdinalIgnoreCase));

#if RC_TEST_TOOLS
static string Slug(string value)
{
    var chars = value.Where(char.IsLetterOrDigit).Take(32).ToArray();
    return chars.Length == 0 ? Guid.NewGuid().ToString("N") : new string(chars).ToLowerInvariant();
}
#endif
