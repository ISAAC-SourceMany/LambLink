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
    public const string BuildTag = "rc10";

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
    private float _nextIndoctrinationUiProbeAt;
    private bool _indoctrinationUiWasVisible;
    private string _lastIndoctrinationProbeSignature = string.Empty;
    private float _nextIndoctrinationCandidateLogAt;

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

    internal static void NotifyIndoctrinationMenuOpened(object[] args)
    {
        var self = _instance;
        if (self == null) return;

        args ??= Array.Empty<object>();
        self.Logger.LogInfo($"[RAFFLE][HOOK] indoctrination menu detected: bridgeConnected={self._bridge?.IsConnected == true}, args={args.Length}");
        self.EnsureBridgeStarted("indoctrination-hook");

        if (self._followers == null || self._saves == null)
        {
            self.Logger.LogWarning("[RAFFLE][HOOK] services are not initialized; raffle request skipped.");
            return;
        }

        var recruitId = self._followers.ResolveIndoctrinationRecruitId(args, out var source);
        if (!recruitId.HasValue)
        {
            var argTypes = string.Join(", ", args.Where(x => x != null).Select(x => x.GetType().FullName));
            self.Logger.LogWarning($"Indoctrination menu opened but recruit ID could not be resolved. args=[{argTypes}]");
            return;
        }

        if (self._bridge?.IsConnected != true)
        {
            self.Logger.LogWarning($"[RAFFLE][HOOK] recruit {recruitId.Value} resolved (source={source}) but Companion bridge is not connected; raffle request skipped. Re-open indoctrination after [BRIDGE][CONNECTED] appears.");
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

    // Runtime fallback for builds where neither explicit Harmony hook fires.
    // RC10 deliberately does not depend on one exact GameObject/controller name. While a
    // vanilla recruit is pending, scan active MonoBehaviours/GameObjects whose runtime names
    // clearly look like the indoctrination/appearance-form UI. This keeps the production rule
    // intact: a mere pending recruit never opens a raffle; the matching UI must actually be active.
    private void ProbeIndoctrinationUiFallback()
    {
        var now = UnityEngine.Time.unscaledTime;
        if (now < _nextIndoctrinationUiProbeAt) return;
        _nextIndoctrinationUiProbeAt = now + 0.25f;

        var pendingIds = _followers?.GetPendingRecruitIds() ?? Array.Empty<int>();
        if (pendingIds.Count == 0)
        {
            if (_indoctrinationUiWasVisible)
                Logger.LogInfo("[RAFFLE][FALLBACK] pending recruit/UI state cleared; trigger re-armed.");
            _indoctrinationUiWasVisible = false;
            _lastIndoctrinationProbeSignature = string.Empty;
            return;
        }

        bool visible = false;
        object? detectedInstance = null;
        string detectedName = string.Empty;
        var candidates = new List<string>();

        try
        {
            // First keep the precise known controller path.
            var formType = AccessTools.TypeByName("Lamb.UI.UIAppearanceMenuController_Form");
            if (formType != null)
            {
                foreach (var instance in UnityEngine.Object.FindObjectsOfType(formType))
                {
                    if (instance is not UnityEngine.Component component || !component.gameObject.activeInHierarchy) continue;
                    visible = true;
                    detectedInstance = instance;
                    detectedName = $"{formType.FullName}@{GetHierarchyPath(component.transform)}";
                    candidates.Add(detectedName);
                    break;
                }
            }

            // RC10 broad runtime discovery. The actual game build can wrap/rename the form
            // controller, so inspect active behaviours instead of guessing one more method name.
            if (!visible)
            {
                var behaviours = UnityEngine.Object.FindObjectsOfType<UnityEngine.MonoBehaviour>();
                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null || !behaviour.gameObject.activeInHierarchy) continue;
                    var typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
                    var objectName = behaviour.gameObject.name ?? string.Empty;
                    if (!LooksLikeIndoctrinationUi(typeName, objectName)) continue;

                    var label = $"{typeName}@{GetHierarchyPath(behaviour.transform)}";
                    candidates.Add(label);
                    if (!visible)
                    {
                        visible = true;
                        detectedInstance = behaviour;
                        detectedName = label;
                    }
                }
            }

            // Last resort: active GameObject names. Resources.FindObjectsOfTypeAll includes
            // inactive objects too, therefore require activeInHierarchy before considering it.
            if (!visible)
            {
                foreach (var go in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>())
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    if (!LooksLikeIndoctrinationUi(string.Empty, go.name ?? string.Empty)) continue;
                    var label = $"GameObject@{GetHierarchyPath(go.transform)}";
                    candidates.Add(label);
                    if (!visible)
                    {
                        visible = true;
                        detectedInstance = go;
                        detectedName = label;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[RAFFLE][FALLBACK] UI probe failed: {ex.GetBaseException().Message}");
        }

        var signature = $"pending=[{string.Join(",", pendingIds)}]|visible={visible}|{string.Join(" || ", candidates.Take(8))}";
        if (!string.Equals(signature, _lastIndoctrinationProbeSignature, StringComparison.Ordinal) || now >= _nextIndoctrinationCandidateLogAt)
        {
            _lastIndoctrinationProbeSignature = signature;
            _nextIndoctrinationCandidateLogAt = now + 5f;
            Logger.LogInfo($"[RAFFLE][PROBE] {signature}");
        }

        if (visible && !_indoctrinationUiWasVisible)
        {
            Logger.LogInfo($"[RAFFLE][FALLBACK] indoctrination UI became visible: object='{detectedName}', pending=[{string.Join(",", pendingIds)}], bridgeConnected={_bridge?.IsConnected == true}");
            NotifyIndoctrinationMenuOpened(detectedInstance == null ? Array.Empty<object>() : new[] { detectedInstance });
        }
        else if (!visible && _indoctrinationUiWasVisible)
        {
            Logger.LogInfo("[RAFFLE][FALLBACK] indoctrination UI closed; trigger re-armed.");
        }

        _indoctrinationUiWasVisible = visible;
    }

    private static bool LooksLikeIndoctrinationUi(string typeName, string objectName)
    {
        static bool Has(string value, string token) => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        if (Has(typeName, "Indoctr") || Has(objectName, "Indoctr")) return true;
        if ((Has(typeName, "AppearanceMenu") || Has(objectName, "Appearance Menu") || Has(objectName, "AppearanceMenu")) &&
            (Has(typeName, "Form") || Has(objectName, "Follower") || Has(objectName, "Form"))) return true;
        if (Has(typeName, "UIAppearanceMenuController_Form")) return true;
        return false;
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

        ProbeIndoctrinationUiFallback();

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
