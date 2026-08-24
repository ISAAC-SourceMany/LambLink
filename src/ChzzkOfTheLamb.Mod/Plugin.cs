using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using HarmonyLib;
using ChzzkOfTheLamb.Mod.Game;
using ChzzkOfTheLamb.Mod.Network;
using ChzzkOfTheLamb.Protocol;
using Newtonsoft.Json;

namespace ChzzkOfTheLamb.Mod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    private static Plugin? _instance;
    public const string PluginGuid = "com.chzzkofthelamb.integration";
    public const string PluginName = "CHZZK Companion Integration";
    public const string PluginVersion = "1.0.0";
    public const string BuildTag = "rc18-state-sync-auto-unlock";

    private readonly ConcurrentQueue<GameCommandEnvelope> _queue = new();
    private ModBridgeClient? _bridge;
    private FollowerService? _followers;
    private FollowerAppearanceService? _appearances;
    private DonationEffectService? _donations;
    private GameSaveService? _saves;
    private float _nextStatusAt;
    private float _nextRecruitScanAt;
    private readonly HashSet<int> _announcedRecruitIds = new();
    private readonly HashSet<int> _handledRecruitIds = new();
    private readonly Dictionary<int, RaffleRequestedEvent> _pendingRaffleRequests = new();
    private readonly Dictionary<int, float> _pendingRaffleQueuedAt = new();
    private bool _bridgeStarted;
    private volatile bool _initialStateSyncPending;
    private Task<bool>? _statusSendTask;
    private string _statusSendReason = string.Empty;
    private GameStatusEvent? _statusSendPayload;
    private Task<bool>? _raffleSendTask;
    private int? _raffleSendRecruitId;
    private float _nextRaffleSendAt;

    private void Awake()
    {
        _instance = this;
        _saves = new GameSaveService(Logger);
        _appearances = new FollowerAppearanceService(Logger, _saves);
        _followers = new FollowerService(Logger, _saves, _appearances);
        _donations = new DonationEffectService(Logger);
        _bridge = new ModBridgeClient(_queue, Logger);
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

        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded [BUILD={BuildTag}]");

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
        _ = _bridge.RunAsync(CancellationToken.None);
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
        if (self == null || self._followers == null || self._saves == null)
            return;

        self.Logger.LogInfo($"[RAFFLE][PATCH] ShowIndoctrinationMenu Prefix fired; args={args.Length}, bridgeConnected={self._bridge?.IsConnected == true}");

        var recruitId = self._followers.ResolveIndoctrinationRecruitId(args, out var source);
        if (!recruitId.HasValue)
        {
            var argTypes = string.Join(", ", args.Where(x => x != null).Select(x => x.GetType().FullName));
            self.Logger.LogWarning($"Indoctrination menu opened but recruit ID could not be resolved. args=[{argTypes}]");
            return;
        }

        if (self._handledRecruitIds.Contains(recruitId.Value)
            || self._announcedRecruitIds.Contains(recruitId.Value)
            || self._pendingRaffleRequests.ContainsKey(recruitId.Value))
        {
            self.Logger.LogInfo($"Indoctrination raffle trigger ignored for recruit {recruitId.Value} (already handled/announced/pending).");
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

    // Unity main thread: all Cult of the Lamb API calls are dispatched here.
    private void Update()
    {
        try { _followers?.Tick(); }
        catch (Exception ex) { Logger.LogError($"[UPDATE][FOLLOWER-TICK] {ex}"); }

        PollStatusSendCompletion();
        PollRaffleSendCompletion();

        while (_queue.TryDequeue(out var command))
        {
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
                    case GameMessageTypes.GetGameStatus:
                        Logger.LogInfo("[BRIDGE][STATE] GET_GAME_STATUS received from Companion");
                        _initialStateSyncPending = true;
                        break;
                    case GameMessageTypes.GetFollowerRoster:
                    {
                        if (PlayerFarming.Instance == null)
                        {
                            _ = _bridge!.SendAsync(GameMessageTypes.FollowerRoster, new FollowerRosterSnapshot
                            {
                                SaveId = "unknown"
                            });
                            break;
                        }

                        var roster = _followers!.BuildRoster();
                        Logger.LogInfo($"[FOLLOWER-ROSTER][TX] save={roster.SaveId}, followers={roster.Followers.Count}");
                        _ = _bridge!.TrySendAsync(GameMessageTypes.FollowerRoster, roster);
                        break;
                    }
                    case GameMessageTypes.GetAppearanceCatalog:
                    {
                        // Do not inspect WorshipperData while the game is still in Splash/Main Menu.
                        // Some COTL singletons are intentionally unavailable during boot.
                        if (PlayerFarming.Instance == null)
                        {
                            _ = _bridge!.SendAsync(GameMessageTypes.AppearanceCatalog, new FollowerAppearanceCatalog
                            {
                                SaveId = "unknown"
                            });
                            break;
                        }

                        var req = JsonConvert.DeserializeObject<AppearanceCatalogRequest>(command.PayloadJson) ?? new AppearanceCatalogRequest();
                        var catalog = _appearances!.BuildCatalog(req.IncludeModded, req.IncludeSpecial);
                        Logger.LogInfo($"[APPEARANCE][TX] save={catalog.SaveId}, forms={catalog.Forms.Count}");
                        _ = _bridge!.TrySendAsync(GameMessageTypes.AppearanceCatalog, catalog);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        // Pending recruits are only scanned for lifecycle cleanup.
        // The raffle itself is triggered by UIManager.ShowIndoctrinationMenu via Harmony,
        // i.e. when the streamer actually starts indoctrinating a recruit.
        if (UnityEngine.Time.unscaledTime >= _nextRecruitScanAt)
        {
            _nextRecruitScanAt = UnityEngine.Time.unscaledTime + 1f;
            if (PlayerFarming.Instance != null)
            {
                var live = new HashSet<int>(_followers!.GetPendingRecruitIds());
                _announcedRecruitIds.RemoveWhere(id => !live.Contains(id));
                _handledRecruitIds.RemoveWhere(id => !live.Contains(id));
                foreach (var staleId in _pendingRaffleRequests.Keys
                             .Where(id => !live.Contains(id)
                                          && (!_pendingRaffleQueuedAt.TryGetValue(id, out var queuedAt)
                                              || UnityEngine.Time.unscaledTime - queuedAt >= 3f))
                             .ToList())
                {
                    _pendingRaffleRequests.Remove(staleId);
                    _pendingRaffleQueuedAt.Remove(staleId);
                    Logger.LogInfo($"[RAFFLE][QUEUE] dropped stale recruit={staleId}; recruit is no longer pending");
                }
            }
        }

        TryStartPendingRaffleSend();

        // Low-frequency status heartbeat for the Companion. Avoids network threads touching game APIs.
        if (_initialStateSyncPending && _bridge?.IsConnected == true && _statusSendTask == null)
        {
            _initialStateSyncPending = false;
            TryStartGameStatusSend("connection-handshake");
        }

        if (UnityEngine.Time.unscaledTime >= _nextStatusAt)
        {
            _nextStatusAt = UnityEngine.Time.unscaledTime + 5f;
            TryStartGameStatusSend("periodic-heartbeat");
        }
    }

    private void TryStartGameStatusSend(string reason)
    {
        if (_bridge?.IsConnected != true || _statusSendTask != null) return;

        var status = BuildGameStatusSafely();
        _statusSendReason = reason;
        _statusSendPayload = status;
        Logger.LogInfo($"[BRIDGE][STATE][TX-START] reason={reason}, inGame={status.InGame}, save={status.SaveId}, area={status.Area}");
        _statusSendTask = _bridge.TrySendAsync(GameMessageTypes.GameStatus, status);
    }

    private GameStatusEvent BuildGameStatusSafely()
    {
        var status = new GameStatusEvent { ModVersion = PluginVersion };
        try { status.InGame = PlayerFarming.Instance != null; }
        catch (Exception ex) { Logger.LogWarning($"[BRIDGE][STATE] in-game probe failed: {ex.GetBaseException().Message}"); }

        if (status.InGame)
        {
            try { status.SaveId = _saves?.GetCurrentSaveId() ?? "unknown"; }
            catch (Exception ex) { Logger.LogWarning($"[BRIDGE][STATE] save probe failed: {ex.GetBaseException().Message}"); }
        }

        try { status.GameVersion = UnityEngine.Application.version ?? string.Empty; }
        catch (Exception ex) { Logger.LogWarning($"[BRIDGE][STATE] game-version probe failed: {ex.GetBaseException().Message}"); }

        try { status.Area = DonationEffectService.GetCurrentArea(); }
        catch (Exception ex)
        {
            status.Area = status.InGame ? "BASE" : "UNKNOWN";
            Logger.LogWarning($"[BRIDGE][STATE] area probe failed; fallback={status.Area}: {ex.GetBaseException().Message}");
        }

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
        _raffleSendTask = _bridge.TrySendAsync(GameMessageTypes.RaffleRequested, request);
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
            _pendingRaffleRequests.Remove(recruitId);
            _pendingRaffleQueuedAt.Remove(recruitId);
            _announcedRecruitIds.Add(recruitId);
            Logger.LogInfo($"CHZZK raffle requested at indoctrination start for game recruit {recruitId}; delivery confirmed");
        }
        else
        {
            _nextRaffleSendAt = UnityEngine.Time.unscaledTime + 1f;
            Logger.LogWarning($"[RAFFLE][TX-FAILED] recruit={recruitId}; retrying while recruit remains pending");
        }

        _raffleSendTask = null;
        _raffleSendRecruitId = null;
    }
}
