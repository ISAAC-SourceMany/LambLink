using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    public const string BuildTag = "rc11";

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
    private readonly HashSet<int> _raffleRequestInFlight = new();
    private readonly object _raffleStateGate = new();
    private bool _bridgeStarted;
    private float _nextBridgeStartCheckAt;

    private void Awake()
    {
        _instance = this;
        _saves = new GameSaveService(Logger);
        _appearances = new FollowerAppearanceService(Logger, _saves);
        _followers = new FollowerService(Logger, _saves, _appearances);
        _donations = new DonationEffectService(Logger);
        _bridge = new ModBridgeClient(_queue, Logger);

        var harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
        IndoctrinationRafflePatch.Install(harmony);
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded [BUILD={BuildTag}]");

        // The bridge is pure localhost networking. Starting it in Awake is safe because all
        // game API mutations still remain queued and are executed from Update on Unity's
        // main thread. Do not gate the socket connection on SceneManager.GetActiveScene():
        // some COTL scene configurations keep an unexpected active-scene value long after
        // the game is playable, which can leave the Companion waiting forever.
        EnsureBridgeStarted("plugin-awake");
    }

    private void EnsureBridgeStarted(string reason)
    {
        if (_bridgeStarted || _bridge == null) return;
        _bridgeStarted = true;
        Logger.LogInfo($"[BRIDGE][START] reason={reason}, endpoint=ws://127.0.0.1:17771/game");
        _ = _bridge.RunAsync(CancellationToken.None);
    }

    internal static void LogRafflePatchDiagnostic(string message)
    {
        _instance?.Logger.LogInfo($"[RAFFLE][PATCH] {message}");
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

    internal static void NotifyRecruitInteractionStarted(object[] args, string methodName)
    {
        var self = _instance;
        if (self == null) return;
        self.Logger.LogInfo($"[RAFFLE][INTERACTION] vanilla recruit interaction started: method={methodName}, bridgeConnected={self._bridge?.IsConnected == true}, args={args?.Length ?? 0}");
        self.RequestRaffleForCurrentRecruit(args ?? Array.Empty<object>(), $"interaction:{methodName}");
    }

    internal static void NotifyIndoctrinationMenuOpened(object[] args)
    {
        var self = _instance;
        if (self == null) return;
        self.Logger.LogInfo($"[RAFFLE][UI-FALLBACK] indoctrination UI signal: bridgeConnected={self._bridge?.IsConnected == true}, args={args?.Length ?? 0}");
        self.RequestRaffleForCurrentRecruit(args ?? Array.Empty<object>(), "ui-fallback");
    }

    private void RequestRaffleForCurrentRecruit(object[] args, string triggerSource)
    {
        EnsureBridgeStarted(triggerSource);

        if (_followers == null || _saves == null || _bridge == null)
        {
            Logger.LogWarning($"[RAFFLE][REQUEST] services unavailable; trigger={triggerSource}");
            return;
        }

        var recruitId = _followers.ResolveIndoctrinationRecruitId(args, out var recruitSource);
        if (!recruitId.HasValue)
        {
            var argTypes = string.Join(", ", args.Where(x => x != null).Select(x => x.GetType().FullName));
            Logger.LogWarning($"[RAFFLE][REQUEST] recruit ID unresolved; trigger={triggerSource}, args=[{argTypes}]");
            return;
        }

        if (!_bridge.IsConnected)
        {
            Logger.LogWarning($"[RAFFLE][REQUEST] recruit={recruitId.Value} resolved (source={recruitSource}) but bridge is disconnected; trigger={triggerSource}. Request NOT marked announced.");
            return;
        }

        lock (_raffleStateGate)
        {
            if (_handledRecruitIds.Contains(recruitId.Value) || _announcedRecruitIds.Contains(recruitId.Value) || _raffleRequestInFlight.Contains(recruitId.Value))
            {
                Logger.LogInfo($"[RAFFLE][REQUEST] ignored duplicate recruit={recruitId.Value}; trigger={triggerSource}");
                return;
            }
            _raffleRequestInFlight.Add(recruitId.Value);
        }

        var request = new RaffleRequestedEvent
        {
            Reason = "indoctrination_started",
            RecruitFollowerId = recruitId.Value,
            SaveId = _saves.GetCurrentSaveId()
        };

        Logger.LogInfo($"[RAFFLE][REQUEST] sending recruit={recruitId.Value}, trigger={triggerSource}, recruitSource={recruitSource}, save={request.SaveId}");
        _ = SendRaffleRequestAsync(recruitId.Value, request, triggerSource);
    }

    private async System.Threading.Tasks.Task SendRaffleRequestAsync(int recruitId, RaffleRequestedEvent request, string triggerSource)
    {
        var sent = false;
        try
        {
            sent = _bridge != null && await _bridge.TrySendAsync(GameMessageTypes.RaffleRequested, request);
            if (sent)
            {
                lock (_raffleStateGate) _announcedRecruitIds.Add(recruitId);
                Logger.LogInfo($"CHZZK raffle requested at recruit interaction start for game recruit {recruitId} (trigger={triggerSource})");
            }
            else
            {
                Logger.LogWarning($"[RAFFLE][REQUEST] send failed/not connected for recruit={recruitId}; trigger={triggerSource}. Trigger remains retryable.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[RAFFLE][REQUEST] send exception for recruit={recruitId}: {ex.GetBaseException().Message}. Trigger remains retryable.");
        }
        finally
        {
            lock (_raffleStateGate) _raffleRequestInFlight.Remove(recruitId);
        }
    }

    private static string GetHierarchyPath(UnityEngine.Transform transform)
    {
        try
        {
            var parts = new Stack<string>();
            var current = transform;
            var guard = 0;
            while (current != null && guard++ < 12)
            {
                parts.Push(current.gameObject.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }
        catch
        {
            return transform?.gameObject?.name ?? "(unknown)";
        }
    }

    // Unity main thread: all Cult of the Lamb API calls are dispatched here.
    private void Update()
    {
        _followers?.Tick();

        // Fallback only. The normal path starts the bridge in Awake. Keeping this check
        // protects against an unexpected initialization failure without depending on scene names.
        if (!_bridgeStarted && UnityEngine.Time.unscaledTime >= _nextBridgeStartCheckAt)
        {
            _nextBridgeStartCheckAt = UnityEngine.Time.unscaledTime + 1f;
            EnsureBridgeStarted("update-fallback");
        }

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
                            lock (_raffleStateGate) _handledRecruitIds.Add(result.FollowerId.Value);
                        _ = _bridge!.SendAsync(GameMessageTypes.FollowerSpawnResult, result);
                        break;
                    }
                    case GameMessageTypes.ApplyRecruitIdentity:
                    {
                        var result = _followers!.ApplyIdentityFromJson(command.PayloadJson);
                        if (result.Success) lock (_raffleStateGate) _handledRecruitIds.Add(result.RecruitFollowerId);
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
                        _ = _bridge!.SendAsync(GameMessageTypes.FollowerRoster, roster);
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
                        _ = _bridge!.SendAsync(GameMessageTypes.AppearanceCatalog, catalog);
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
        // RC11 starts raffles from the vanilla FollowerRecruit interaction lifecycle.
        // UI hooks are compatibility fallbacks only.
        if (UnityEngine.Time.unscaledTime >= _nextRecruitScanAt)
        {
            _nextRecruitScanAt = UnityEngine.Time.unscaledTime + 1f;
            if (PlayerFarming.Instance != null)
            {
                var live = new HashSet<int>(_followers!.GetPendingRecruitIds());
                lock (_raffleStateGate)
                {
                    _announcedRecruitIds.RemoveWhere(id => !live.Contains(id));
                    _handledRecruitIds.RemoveWhere(id => !live.Contains(id));
                    _raffleRequestInFlight.RemoveWhere(id => !live.Contains(id));
                }
            }
        }

        // Low-frequency status heartbeat for the Companion. Avoids network threads touching game APIs.
        if (UnityEngine.Time.unscaledTime >= _nextStatusAt)
        {
            _nextStatusAt = UnityEngine.Time.unscaledTime + 5f;
            if (_bridge?.IsConnected == true)
            {
                _ = _bridge.SendAsync(GameMessageTypes.GameStatus, new GameStatusEvent
                {
                    InGame = PlayerFarming.Instance != null,
                    SaveId = PlayerFarming.Instance != null ? _saves!.GetCurrentSaveId() : "unknown",
                    ModVersion = PluginVersion,
                    GameVersion = UnityEngine.Application.version,
                    Area = DonationEffectService.GetCurrentArea()
                });
            }
        }
    }
}
