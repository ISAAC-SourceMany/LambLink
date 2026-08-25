using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using ChzzkOfTheLamb.Mod.Game;
using ChzzkOfTheLamb.Mod.Network;
using ChzzkOfTheLamb.Protocol;
using Newtonsoft.Json;

namespace ChzzkOfTheLamb.Mod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(CotlApiGuid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    private static Plugin? _instance;
    public const string PluginGuid = "com.chzzkofthelamb.integration";
    public const string PluginName = "CHZZK Companion Integration";
    public const string PluginVersion = "1.0.0";
    public const string CotlApiGuid = "io.github.xhayper.COTL_API";
    public const string BuildTag = "rc28-inline-nameplate-safe-reconcile";

    private readonly ConcurrentQueue<GameCommandEnvelope> _queue = new();
    private readonly CancellationTokenSource _runtimeLifetime = new();
    private int _runtimeShutdownRequested;
    private ModBridgeClient? _bridge;
    private FollowerService? _followers;
    private FollowerAppearanceService? _appearances;
    private DonationEffectService? _donations;
    private GameSaveService? _saves;
    private float _nextStatusAt;
    private readonly HashSet<int> _announcedRecruitIds = new();
    private readonly Dictionary<int, float> _announcedRecruitAt = new();
    private readonly HashSet<int> _handledRecruitIds = new();
    private readonly Dictionary<int, RaffleRequestedEvent> _pendingRaffleRequests = new();
    private readonly Dictionary<int, float> _pendingRaffleQueuedAt = new();
    private bool _bridgeStarted;
    private volatile bool _initialStateSyncPending;
    private System.Threading.Tasks.Task<bool>? _statusSendTask;
    private string _statusSendReason = string.Empty;
    private GameStatusEvent? _statusSendPayload;
    private System.Threading.Tasks.Task<bool>? _raffleSendTask;
    private int? _raffleSendRecruitId;
    private float _nextRaffleSendAt;
    private volatile string _cachedSaveId = "unknown";
    private volatile string _cachedArea = "UNKNOWN";
    private volatile bool _cachedInGame;
    private int _networkFallbackStatusInFlight;
    private int _mainThreadId;
    private long _updateCount;
    private long _lastUpdateTimestamp;
    private volatile string _diagnosticStage = "AWAKE";
    private long _diagnosticStageStarted;
    private long _diagnosticProgress;

    private void Awake()
    {
        _instance = this;
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        _diagnosticStageStarted = Stopwatch.GetTimestamp();
        _saves = new GameSaveService(Logger);
        _appearances = new FollowerAppearanceService(Logger, _saves, SetDiagnosticStage);
        _followers = new FollowerService(Logger, _saves, _appearances);
        _donations = new DonationEffectService(Logger);
        _bridge = new ModBridgeClient(_queue, Logger);
        _bridge.CommandDecoded += OnBridgeCommandDecoded;
        _bridge.ConnectionChanged += connected =>
        {
            if (connected)
            {
                _initialStateSyncPending = true;
                Logger.LogInfo("[BRIDGE][STATE] initial synchronization requested after connect");
            }
            else
            {
                Logger.LogInfo("[BRIDGE][STATE] connection closed; synchronization will retry after reconnect");
            }
        };

        BridgeRuntimeHost.Install(RunMainThreadUpdate, RequestRuntimeShutdown, Logger);

        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
        IndoctrinationRafflePatch.VerifyInstallation(PluginGuid);
        FollowerNameplatePatch.VerifyInstallation(PluginGuid, Logger);
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded [BUILD={BuildTag}]");
        Logger.LogInfo($"[DEPENDENCY] COTL_API={CotlApiGuid} hard dependency loaded before CHZZK integration");
        Logger.LogInfo($"[DIAG][BOOT] mainThread={_mainThreadId}, runtimeHost=persistent-game-object, watchdog=enabled, network-cache-fallback=enabled");
        Logger.LogInfo("[BRIDGE][MAIN-THREAD] persistent runtime host dispatches commands before optional follower maintenance");
        _ = RunDiagnosticWatchdogAsync(_runtimeLifetime.Token);

        // The bridge performs localhost networking only. Game mutations remain queued and
        // are still executed by Update on Unity's main thread. Start immediately instead of
        // waiting for an active-scene name: some COTL configurations keep Splash/blank as
        // the active scene after the game becomes playable, which prevented RC15 from ever
        // attempting the Companion connection.
        EnsureBridgeStarted("plugin-awake");
    }

    private void EnsureBridgeStarted(string reason)
    {
        if (_bridgeStarted || _bridge == null) return;
        _bridgeStarted = true;
        Logger.LogInfo($"[BRIDGE][START] reason={reason}, endpoint=ws://127.0.0.1:17771/game");
        _ = _bridge.RunAsync(_runtimeLifetime.Token);
    }

    private void OnDestroy()
    {
        // The game's startup lifecycle can destroy/disable the BepInEx plugin component even
        // though the integration must remain alive. The independent runtime host and bridge are
        // intentionally not cancelled here.
        Logger.LogWarning("[DIAG][PLUGIN-DESTROY] BepInEx plugin component destroyed; persistent runtime host and bridge remain active");
    }

    private void RequestRuntimeShutdown()
    {
        if (Interlocked.Exchange(ref _runtimeShutdownRequested, 1) != 0) return;
        Logger.LogInfo("[DIAG][RUNTIME-SHUTDOWN] persistent host requested bridge/watchdog shutdown");
        try { _runtimeLifetime.Cancel(); }
        catch (Exception ex) { Logger.LogWarning($"[DIAG][RUNTIME-SHUTDOWN-FAILED] {ex.GetBaseException().Message}"); }
    }

    // Runs on the WebSocket receive thread. It never calls Unity or game APIs.
    private void OnBridgeCommandDecoded(long sequence, GameCommandEnvelope command)
    {
        Logger.LogInfo($"[BRIDGE][RX][CALLBACK] id={sequence}, type={command.Type}, thread={Thread.CurrentThread.ManagedThreadId}, mainThread={_mainThreadId}");
        if (!string.Equals(command.Type, GameMessageTypes.GetGameStatus, StringComparison.Ordinal)) return;
        _ = SendCachedGameStatusFallbackAsync(sequence);
    }

    private async System.Threading.Tasks.Task SendCachedGameStatusFallbackAsync(long receiveSequence)
    {
        if (Interlocked.CompareExchange(ref _networkFallbackStatusInFlight, 1, 0) != 0)
        {
            Logger.LogInfo($"[BRIDGE][STATE][FALLBACK-SKIPPED] rx={receiveSequence}, reason=already-in-flight");
            return;
        }

        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                if (attempt > 1) await System.Threading.Tasks.Task.Delay(attempt == 2 ? 1000 : 2000, _runtimeLifetime.Token);
                var saveId = _saves?.LastResolvedSaveId ?? "unknown";
                if (string.Equals(saveId, "unknown", StringComparison.Ordinal)) saveId = _cachedSaveId;
                var inGame = _cachedInGame || !string.Equals(saveId, "unknown", StringComparison.Ordinal);
                var updateCount = Interlocked.Read(ref _updateCount);
                var lastUpdate = Volatile.Read(ref _lastUpdateTimestamp);
                var pumpActive = updateCount > 0 && lastUpdate > 0
                                 && (Stopwatch.GetTimestamp() - lastUpdate) * 1000.0 / Stopwatch.Frequency < 5000;
                var status = new GameStatusEvent
                {
                    InGame = inGame,
                    SaveId = saveId,
                    ModVersion = PluginVersion,
                    GameVersion = string.Empty,
                    Area = inGame ? "BASE" : _cachedArea,
                    RuntimePumpActive = pumpActive,
                    RuntimeUpdateCount = updateCount
                };
                Logger.LogInfo($"[BRIDGE][STATE][FALLBACK-TX-START] rx={receiveSequence}, attempt={attempt}/3, pump={status.RuntimePumpActive}, updates={status.RuntimeUpdateCount}, inGame={status.InGame}, save={status.SaveId}, source=thread-safe-cache");
                var sent = _bridge != null && await _bridge.TrySendAsync(GameMessageTypes.GameStatus, status, _runtimeLifetime.Token);
                Logger.LogInfo($"[BRIDGE][STATE][FALLBACK-TX-{(sent ? "OK" : "FAILED")}] rx={receiveSequence}, attempt={attempt}/3, inGame={status.InGame}, save={status.SaveId}");
                if (!sent || !string.Equals(saveId, "unknown", StringComparison.Ordinal)) break;
            }
        }
        catch (OperationCanceledException) when (_runtimeLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogError($"[BRIDGE][STATE][FALLBACK-TX-FAILED] rx={receiveSequence}: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _networkFallbackStatusInFlight, 0);
        }
    }

    internal static void RefreshFollowerNameplate(object uiFollowerName)
    {
        try
        {
            _instance?._followers?.RefreshFollowerNameplate(uiFollowerName);
        }
        catch (Exception ex)
        {
            _instance?.Logger.LogWarning($"CHZZK follower nameplate patch failed: {ex.GetBaseException().Message}");
        }
    }

    internal static void NotifyIndoctrinationMenuOpened(object[] args)
    {
        var self = _instance;
        if (ReferenceEquals(self, null) || self._followers == null || self._saves == null)
            return;

        self.Logger.LogInfo($"[RAFFLE][PATCH] ShowIndoctrinationMenu Prefix fired; args={args.Length}, bridgeConnected={self._bridge?.IsConnected == true}");

        var recruitId = self._followers.ResolveIndoctrinationRecruitId(args, out var source);
        if (!recruitId.HasValue || recruitId.Value <= 0)
        {
            var argTypes = string.Join(", ", args.Where(x => x != null).Select(x => x.GetType().FullName));
            self.Logger.LogWarning($"Indoctrination menu opened but a positive recruit ID could not be resolved. value={recruitId?.ToString() ?? "null"}, args=[{argTypes}]");
            return;
        }

        var handled = self._handledRecruitIds.Contains(recruitId.Value);
        var announced = self._announcedRecruitIds.Contains(recruitId.Value);
        var pending = self._pendingRaffleRequests.ContainsKey(recruitId.Value);
        if (handled || announced || pending)
        {
            self.Logger.LogInfo($"[RAFFLE][GUARD] recruit={recruitId.Value}, handled={handled}, activeOrAnnounced={announced}, pendingDelivery={pending}; duplicate menu callback ignored");
            return;
        }

        self._pendingRaffleRequests[recruitId.Value] = new RaffleRequestedEvent
        {
            Reason = "indoctrination_started",
            RecruitFollowerId = recruitId.Value,
            SaveId = self._saves.GetCurrentSaveId()
        };
        self._pendingRaffleQueuedAt[recruitId.Value] = UnityEngine.Time.unscaledTime;
        self.Logger.LogInfo($"[RAFFLE][QUEUE] recruit={recruitId.Value}, source={source}, bridgeConnected={self._bridge?.IsConnected == true}; request will retry until delivered");
    }

    internal static void LogRafflePatchTarget(MethodBase? target)
    {
        var signature = target == null
            ? "NOT_FOUND"
            : $"{target.DeclaringType?.FullName}.{target.Name}({string.Join(",", target.GetParameters().Select(p => p.ParameterType.Name))})";
        _instance?.Logger.LogInfo($"[RAFFLE][PATCH] automatic Harmony target={signature}");
    }

    internal static void LogRafflePatchVerification(MethodBase target, bool installed, string owners)
    {
        var signature = $"{target.DeclaringType?.FullName}.{target.Name}({string.Join(",", target.GetParameters().Select(p => p.ParameterType.Name))})";
        _instance?.Logger.LogInfo($"[RAFFLE][PATCH-VERIFY] target={signature}, owner={PluginGuid}, installed={installed}, prefixOwners=[{owners}]");
    }

    // Unity main thread: all Cult of the Lamb API calls are dispatched here.
    internal void RunMainThreadUpdate()
    {
        var updateNumber = Interlocked.Increment(ref _updateCount);
        Volatile.Write(ref _lastUpdateTimestamp, Stopwatch.GetTimestamp());
        if (updateNumber == 1)
            Logger.LogInfo($"[DIAG][UPDATE][FIRST] source=persistent-runtime-host, thread={Thread.CurrentThread.ManagedThreadId}, expectedMainThread={_mainThreadId}, queue={_queue.Count}");

        SetDiagnosticStage("UPDATE/POLL-COMPLETIONS");
        PollStatusSendCompletion();
        PollRaffleSendCompletion();

        // Mandatory transport dispatch comes first. Optional follower/UI maintenance is last so
        // no gameplay scan can prevent GET_GAME_STATUS or catalog commands from being observed.
        SetDiagnosticStage("UPDATE/QUEUE-DRAIN");
        while (_queue.TryDequeue(out var command))
        {
            var dispatchStarted = Stopwatch.GetTimestamp();
            var sequence = command.DiagnosticSequence;
            Logger.LogInfo($"[BRIDGE][DISPATCH][BEGIN] id={sequence}, type={command.Type}, queueRemaining={_queue.Count}, thread={Thread.CurrentThread.ManagedThreadId}");
            SetDiagnosticStage($"COMMAND/{sequence}/{command.Type}");
            try
            {
                switch (command.Type)
                {
                    case GameMessageTypes.SpawnFollower:
                    {
                        // Development-only transport/API test. Production raffles never create a second recruit.
                        var result = _followers!.SpawnFromJson(command.PayloadJson);
                        if (result.Success && result.FollowerId.HasValue)
                            _handledRecruitIds.Add(result.FollowerId.Value);
                        _ = _bridge!.SendAsync(GameMessageTypes.FollowerSpawnResult, result);
                        break;
                    }
                    case GameMessageTypes.ApplyRecruitIdentity:
                    {
                        var result = _followers!.ApplyIdentityFromJson(command.PayloadJson);
                        if (result.Success) _handledRecruitIds.Add(result.RecruitFollowerId);
                        _ = _bridge!.SendAsync(GameMessageTypes.RecruitIdentityResult, result);
                        break;
                    }
                    case GameMessageTypes.DonationEffect:
                    {
                        var result = _donations!.ApplyFromJson(command.PayloadJson);
                        _ = _bridge!.SendAsync(GameMessageTypes.DonationEffectResult, result);
                        break;
                    }
                    case GameMessageTypes.SyncChzzkFollowerMarkers:
                        _followers!.SyncChzzkFollowerMarkersFromJson(command.PayloadJson);
                        break;
                    case GameMessageTypes.RaffleRequestAck:
                    {
                        var ack = JsonConvert.DeserializeObject<RaffleRequestAck>(command.PayloadJson)
                                  ?? throw new InvalidOperationException("Invalid raffle request acknowledgement.");
                        HandleRaffleRequestAck(ack);
                        break;
                    }
                    case GameMessageTypes.RaffleRoundClosed:
                    {
                        var closed = JsonConvert.DeserializeObject<RaffleRoundClosed>(command.PayloadJson)
                                     ?? throw new InvalidOperationException("Invalid raffle round-closed notification.");
                        HandleRaffleRoundClosed(closed);
                        break;
                    }
                    case GameMessageTypes.GetGameStatus:
                        Logger.LogInfo($"[BRIDGE][STATE] GET_GAME_STATUS dispatched on Unity main thread; id={sequence}");
                        _initialStateSyncPending = true;
                        break;
                    case GameMessageTypes.GetFollowerRoster:
                    {
                        SetDiagnosticStage($"COMMAND/{sequence}/ROSTER/READY-CHECK");
                        if (PlayerFarming.Instance == null)
                        {
                            _ = _bridge!.SendAsync(GameMessageTypes.FollowerRoster, new FollowerRosterSnapshot
                            {
                                SaveId = "unknown"
                            });
                            break;
                        }

                        SetDiagnosticStage($"COMMAND/{sequence}/ROSTER/BUILD");
                        var roster = _followers!.BuildRoster();
                        Logger.LogInfo($"[FOLLOWER-ROSTER][TX] save={roster.SaveId}, followers={roster.Followers.Count}");
                        SetDiagnosticStage($"COMMAND/{sequence}/ROSTER/TX-QUEUE");
                        _ = _bridge!.TrySendAsync(GameMessageTypes.FollowerRoster, roster);
                        break;
                    }
                    case GameMessageTypes.GetAppearanceCatalog:
                    {
                        // Do not inspect WorshipperData while the game is still in Splash/Main Menu.
                        // Some COTL singletons are intentionally unavailable during boot.
                        SetDiagnosticStage($"COMMAND/{sequence}/CATALOG/READY-CHECK");
                        if (PlayerFarming.Instance == null)
                        {
                            _ = _bridge!.SendAsync(GameMessageTypes.AppearanceCatalog, new FollowerAppearanceCatalog
                            {
                                SaveId = "unknown"
                            });
                            break;
                        }

                        SetDiagnosticStage($"COMMAND/{sequence}/CATALOG/DESERIALIZE");
                        var req = JsonConvert.DeserializeObject<AppearanceCatalogRequest>(command.PayloadJson) ?? new AppearanceCatalogRequest();
                        SetDiagnosticStage($"COMMAND/{sequence}/CATALOG/BUILD");
                        var catalog = _appearances!.BuildCatalog(req.IncludeModded, req.IncludeSpecial);
                        Logger.LogInfo($"[APPEARANCE][TX] save={catalog.SaveId}, forms={catalog.Forms.Count}");
                        SetDiagnosticStage($"COMMAND/{sequence}/CATALOG/TX-QUEUE");
                        _ = _bridge!.TrySendAsync(GameMessageTypes.AppearanceCatalog, catalog);
                        break;
                    }
                    default:
                        Logger.LogWarning($"[BRIDGE][DISPATCH][UNKNOWN] id={sequence}, type={command.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[BRIDGE][DISPATCH][FAILED] id={sequence}, type={command.Type}: {ex}");
            }
            finally
            {
                Logger.LogInfo($"[BRIDGE][DISPATCH][END] id={sequence}, type={command.Type}, elapsedMs={ElapsedMilliseconds(dispatchStarted):F1}, queueRemaining={_queue.Count}");
                SetDiagnosticStage("UPDATE/QUEUE-DRAIN");
            }
        }

        // Transport/application synchronization must run before any optional gameplay maintenance.
        // RC14-RC19 performed a global FollowerRecruit object scan before this block. On the
        // affected Unity runtime that scan never returned, so queued GET_GAME_STATUS commands and
        // every later catalog request remained unprocessed even though the WebSocket was open.
        if (_initialStateSyncPending && _bridge?.IsConnected == true && _statusSendTask == null)
        {
            _initialStateSyncPending = false;
            TryStartGameStatusSend("connection-handshake");
        }

        SetDiagnosticStage("UPDATE/STATUS-SCHEDULER");
        if (UnityEngine.Time.unscaledTime >= _nextStatusAt)
        {
            _nextStatusAt = UnityEngine.Time.unscaledTime + 5f;
            TryStartGameStatusSend("periodic-heartbeat");
        }

        SetDiagnosticStage("UPDATE/RAFFLE-SCHEDULER");
        ExpireStaleRaffleGuards();
        TryStartPendingRaffleSend();

        SetDiagnosticStage("UPDATE/FOLLOWER-TICK");
        try { _followers?.Tick(); }
        catch (Exception ex) { Logger.LogError($"[UPDATE][FOLLOWER-TICK] {ex}"); }
        finally { SetDiagnosticStage("UPDATE/IDLE"); }
    }

    private void TryStartGameStatusSend(string reason)
    {
        if (_bridge?.IsConnected != true || _statusSendTask != null) return;

        var status = BuildGameStatusSafely(reason);
        _statusSendReason = reason;
        _statusSendPayload = status;
        Logger.LogInfo($"[BRIDGE][STATE][TX-START] reason={reason}, inGame={status.InGame}, save={status.SaveId}, area={status.Area}");
        _statusSendTask = _bridge.TrySendAsync(GameMessageTypes.GameStatus, status, _runtimeLifetime.Token);
    }

    private GameStatusEvent BuildGameStatusSafely(string reason)
    {
        // GAME_STATUS is the gate for every later catalog/roster request. Keep this mandatory
        // handshake limited to probes already proven safe on the user's runtime. RC14-RC18
        // reached GetCurrentSaveId() and then stopped before TX-START while evaluating optional
        // game-version/area metadata. A blocked optional probe must never suppress SAVE/CATALOG.
        //
        // DonationEffectService remains authoritative when a donation is actually applied and
        // corrects a BASE-selected effect to a dungeon effect when necessary, so the conservative
        // BASE value here does not allow an effect to execute in the wrong context.
        var started = Stopwatch.GetTimestamp();
        Logger.LogInfo($"[BRIDGE][STATE][BUILD][BEGIN] reason={reason}, thread={Thread.CurrentThread.ManagedThreadId}");
        var status = new GameStatusEvent
        {
            ModVersion = PluginVersion,
            GameVersion = string.Empty,
            Area = "UNKNOWN",
            RuntimePumpActive = true,
            RuntimeUpdateCount = Interlocked.Read(ref _updateCount)
        };
        SetDiagnosticStage($"STATUS/{reason}/IN-GAME-PROBE");
        try
        {
            status.InGame = PlayerFarming.Instance != null;
            Logger.LogInfo($"[BRIDGE][STATE][BUILD][IN-GAME-OK] reason={reason}, value={status.InGame}");
        }
        catch (Exception ex) { Logger.LogWarning($"[BRIDGE][STATE] in-game probe failed: {ex.GetBaseException().Message}"); }

        if (status.InGame)
        {
            SetDiagnosticStage($"STATUS/{reason}/SAVE-PROBE");
            Logger.LogInfo($"[BRIDGE][STATE][BUILD][SAVE-BEGIN] reason={reason}");
            try
            {
                status.SaveId = _saves?.GetCurrentSaveId() ?? "unknown";
                Logger.LogInfo($"[BRIDGE][STATE][BUILD][SAVE-END] reason={reason}, value={status.SaveId}");
            }
            catch (Exception ex) { Logger.LogWarning($"[BRIDGE][STATE] save probe failed: {ex.GetBaseException().Message}"); }
            status.Area = "BASE";
        }

        _cachedInGame = status.InGame;
        _cachedSaveId = status.SaveId;
        _cachedArea = status.Area;
        Logger.LogInfo($"[BRIDGE][STATE][BUILD][END] reason={reason}, inGame={status.InGame}, save={status.SaveId}, area={status.Area}, elapsedMs={ElapsedMilliseconds(started):F1}");
        SetDiagnosticStage("UPDATE/STATE-SYNC");
        return status;
    }

    private void PollStatusSendCompletion()
    {
        var task = _statusSendTask;
        if (task == null || !task.IsCompleted) return;

        var status = _statusSendPayload;
        var success = false;
        try { success = task.GetAwaiter().GetResult(); }
        catch (Exception ex) { Logger.LogWarning($"[BRIDGE][STATE][TX-FAILED] reason={_statusSendReason}: {ex.GetBaseException().Message}"); }

        if (success)
            Logger.LogInfo($"[BRIDGE][STATE][TX-OK] reason={_statusSendReason}, inGame={status?.InGame}, save={status?.SaveId}, area={status?.Area}");
        else
        {
            Logger.LogWarning($"[BRIDGE][STATE][TX-FAILED] reason={_statusSendReason}; retry scheduled");
            _initialStateSyncPending = true;
        }

        _statusSendTask = null;
        _statusSendPayload = null;
        _statusSendReason = string.Empty;
    }

    private void TryStartPendingRaffleSend()
    {
        if (_bridge?.IsConnected != true || _raffleSendTask != null || _pendingRaffleRequests.Count == 0) return;
        if (UnityEngine.Time.unscaledTime < _nextRaffleSendAt) return;

        var request = _pendingRaffleRequests.Values.OrderBy(x => x.RecruitFollowerId).First();
        _raffleSendRecruitId = request.RecruitFollowerId;
        Logger.LogInfo($"[RAFFLE][TX-START] recruit={request.RecruitFollowerId}, save={request.SaveId}");
        _raffleSendTask = _bridge.TrySendAsync(GameMessageTypes.RaffleRequested, request, _runtimeLifetime.Token);
    }

    private void PollRaffleSendCompletion()
    {
        var task = _raffleSendTask;
        if (task == null || !task.IsCompleted || !_raffleSendRecruitId.HasValue) return;

        var recruitId = _raffleSendRecruitId.Value;
        var success = false;
        try { success = task.GetAwaiter().GetResult(); }
        catch (Exception ex) { Logger.LogWarning($"[RAFFLE][TX-FAILED] recruit={recruitId}: {ex.GetBaseException().Message}"); }

        if (success)
        {
            // A successful WebSocket write is not proof that Companion dispatched the event.
            // Keep the request pending until the application-level acknowledgement arrives.
            _nextRaffleSendAt = UnityEngine.Time.unscaledTime + 2f;
            if (_pendingRaffleRequests.ContainsKey(recruitId))
                Logger.LogInfo($"[RAFFLE][TX-WRITTEN] recruit={recruitId}; awaiting Companion ACK, retryIn=2s");
            else
                Logger.LogInfo($"[RAFFLE][TX-WRITTEN] recruit={recruitId}; ACK already received");
        }
        else
        {
            _nextRaffleSendAt = UnityEngine.Time.unscaledTime + 1f;
            Logger.LogWarning($"[RAFFLE][TX-FAILED] recruit={recruitId}; retrying while recruit remains pending");
        }

        _raffleSendTask = null;
        _raffleSendRecruitId = null;
    }

    private void HandleRaffleRequestAck(RaffleRequestAck ack)
    {
        if (ack.RecruitFollowerId <= 0) return;

        var wasPending = _pendingRaffleRequests.Remove(ack.RecruitFollowerId);
        _pendingRaffleQueuedAt.Remove(ack.RecruitFollowerId);
        if (ack.Accepted)
        {
            _announcedRecruitIds.Add(ack.RecruitFollowerId);
            _announcedRecruitAt[ack.RecruitFollowerId] = UnityEngine.Time.unscaledTime;
            Logger.LogInfo($"[RAFFLE][ACK] recruit={ack.RecruitFollowerId}, accepted=true, status={ack.Status}, wasPending={wasPending}; delivery confirmed by Companion");
        }
        else
        {
            Logger.LogError($"[RAFFLE][ACK] recruit={ack.RecruitFollowerId}, accepted=false, status={ack.Status}, wasPending={wasPending}; request will not be retried");
        }
    }

    private void HandleRaffleRoundClosed(RaffleRoundClosed closed)
    {
        if (closed.RecruitFollowerId <= 0) return;

        _pendingRaffleRequests.Remove(closed.RecruitFollowerId);
        _pendingRaffleQueuedAt.Remove(closed.RecruitFollowerId);
        var wasAnnounced = _announcedRecruitIds.Remove(closed.RecruitFollowerId);
        _announcedRecruitAt.Remove(closed.RecruitFollowerId);
        if (!closed.AllowRetry)
            _handledRecruitIds.Add(closed.RecruitFollowerId);

        Logger.LogInfo($"[RAFFLE][ROUND-CLOSED] recruit={closed.RecruitFollowerId}, status={closed.Status}, allowRetry={closed.AllowRetry}, releasedAnnouncementGuard={wasAnnounced}, handled={_handledRecruitIds.Contains(closed.RecruitFollowerId)}");
    }

    private void ExpireStaleRaffleGuards()
    {
        if (_announcedRecruitAt.Count == 0) return;

        const float staleAfterSeconds = 600f;
        var now = UnityEngine.Time.unscaledTime;
        foreach (var entry in _announcedRecruitAt.ToArray())
        {
            if (now - entry.Value < staleAfterSeconds) continue;
            _announcedRecruitAt.Remove(entry.Key);
            _announcedRecruitIds.Remove(entry.Key);
            Logger.LogWarning($"[RAFFLE][GUARD-EXPIRED] recruit={entry.Key}, ageSeconds={now - entry.Value:F1}; Companion round-close was not received, retry is permitted");
        }
    }

    private void SetDiagnosticStage(string stage)
    {
        _diagnosticStage = stage;
        Volatile.Write(ref _diagnosticStageStarted, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref _diagnosticProgress);
    }

    private async System.Threading.Tasks.Task RunDiagnosticWatchdogAsync(CancellationToken ct)
    {
        var bootTimestamp = Stopwatch.GetTimestamp();
        string? lastReportedKey = null;
        long lastReportedTimestamp = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await System.Threading.Tasks.Task.Delay(2000, ct);
                var now = Stopwatch.GetTimestamp();
                var updateCount = Interlocked.Read(ref _updateCount);
                var lastUpdate = Volatile.Read(ref _lastUpdateTimestamp);
                var sinceBootMs = (now - bootTimestamp) * 1000.0 / Stopwatch.Frequency;
                var sinceUpdateMs = lastUpdate == 0 ? sinceBootMs : (now - lastUpdate) * 1000.0 / Stopwatch.Frequency;
                if (sinceUpdateMs < 5000) continue;

                var stage = _diagnosticStage;
                var stageStarted = Volatile.Read(ref _diagnosticStageStarted);
                var stageMs = stageStarted == 0 ? sinceBootMs : (now - stageStarted) * 1000.0 / Stopwatch.Frequency;
                var key = updateCount == 0 ? "NO-UPDATE" : stage;
                var repeatMs = lastReportedTimestamp == 0 ? double.MaxValue : (now - lastReportedTimestamp) * 1000.0 / Stopwatch.Frequency;
                if (string.Equals(lastReportedKey, key, StringComparison.Ordinal) && repeatMs < 10000) continue;

                lastReportedKey = key;
                lastReportedTimestamp = now;
                if (updateCount == 0)
                {
                    Logger.LogError($"[DIAG][WATCHDOG][NO-UPDATE] elapsedSinceAwakeMs={sinceBootMs:F0}, mainThread={_mainThreadId}, queue={_queue.Count}, socketConnected={_bridge?.IsConnected == true}");
                }
                else
                {
                    Logger.LogError($"[DIAG][WATCHDOG][STALLED] stage={stage}, stageElapsedMs={stageMs:F0}, sinceUpdateEntryMs={sinceUpdateMs:F0}, updates={updateCount}, progress={Interlocked.Read(ref _diagnosticProgress)}, queue={_queue.Count}, socketConnected={_bridge?.IsConnected == true}");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogError($"[DIAG][WATCHDOG][FAILED] {ex}");
        }
    }

    private static double ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
}
