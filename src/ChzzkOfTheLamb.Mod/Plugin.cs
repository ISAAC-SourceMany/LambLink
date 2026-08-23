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
using UnityEngine.SceneManagement;

namespace ChzzkOfTheLamb.Mod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    private static Plugin? _instance;
    public const string PluginGuid = "com.chzzkofthelamb.integration";
    public const string PluginName = "CHZZK Companion Integration";
    public const string PluginVersion = "1.0.0";

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

        // Do not start networking during the Unity Splash scene. COTL initializes several
        // global animation/UI managers during this window and we want the integration to be
        // completely passive until the boot transition has completed.
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
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
        if (self == null || self._bridge?.IsConnected != true || self._followers == null || self._saves == null)
            return;

        var recruitId = self._followers.ResolveIndoctrinationRecruitId(args, out var source);
        if (!recruitId.HasValue)
        {
            var argTypes = string.Join(", ", args.Where(x => x != null).Select(x => x.GetType().FullName));
            self.Logger.LogWarning($"Indoctrination menu opened but recruit ID could not be resolved. args=[{argTypes}]");
            return;
        }

        if (self._handledRecruitIds.Contains(recruitId.Value) || !self._announcedRecruitIds.Add(recruitId.Value))
        {
            self.Logger.LogInfo($"Indoctrination raffle trigger ignored for recruit {recruitId.Value} (already handled/announced).");
            return;
        }

        self.Logger.LogInfo($"CHZZK raffle requested at indoctrination start for game recruit {recruitId.Value} (source={source})");
        _ = self._bridge.SendAsync(GameMessageTypes.RaffleRequested, new RaffleRequestedEvent
        {
            Reason = "indoctrination_started",
            RecruitFollowerId = recruitId.Value,
            SaveId = self._saves.GetCurrentSaveId()
        });
    }

    // Unity main thread: all Cult of the Lamb API calls are dispatched here.
    private void Update()
    {
        _followers?.Tick();

        // Safe-start the bridge only after Cult of the Lamb has left its Splash scene.
        // This also makes startup issues easy to isolate: no Companion networking touches
        // the game while the intro/splash pipeline is still initializing.
        if (!_bridgeStarted && UnityEngine.Time.unscaledTime >= _nextBridgeStartCheckAt)
        {
            _nextBridgeStartCheckAt = UnityEngine.Time.unscaledTime + 0.5f;
            var sceneName = SceneManager.GetActiveScene().name ?? string.Empty;
            if (!string.Equals(sceneName, "Splash", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(sceneName))
            {
                _bridgeStarted = true;
                Logger.LogInfo($"Starting CHZZK Companion bridge after boot (scene={sceneName}).");
                _ = _bridge!.RunAsync(CancellationToken.None);
            }
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
