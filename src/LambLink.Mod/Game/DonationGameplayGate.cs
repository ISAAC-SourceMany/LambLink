using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LambLink.Mod.Game;

internal sealed class DonationGameplayGate
{
    private const float SceneStableSeconds = 1.25f;
    private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly ManualLogSource _log;
    private readonly List<BooleanProbe> _runtimeProbes = new();
    private bool _probesBuilt;
    private int _lastSceneHandle = int.MinValue;
    private int _lastSceneCount = -1;
    private string _lastSceneName = string.Empty;
    private float _sceneStableAfter;
    private string _lastLoggedSignature = string.Empty;
    private long _revision;

    public DonationGameplayGate(ManualLogSource log)
    {
        _log = log;
    }

    public DonationGameplayState Evaluate(int pendingDonations)
    {
        var now = Time.realtimeSinceStartup;
        var scene = SceneManager.GetActiveScene();
        var sceneCount = SceneManager.sceneCount;
        var sceneName = scene.IsValid() ? scene.name ?? string.Empty : string.Empty;
        if (scene.handle != _lastSceneHandle || sceneCount != _lastSceneCount || !string.Equals(sceneName, _lastSceneName, StringComparison.Ordinal))
        {
            _lastSceneHandle = scene.handle;
            _lastSceneCount = sceneCount;
            _lastSceneName = sceneName;
            _sceneStableAfter = now + SceneStableSeconds;
            _revision++;
            _log.LogInfo($"[DONATION][GATE][SCENE] handle={scene.handle}, count={sceneCount}, name='{sceneName}', loaded={scene.isLoaded}; stableAfter={SceneStableSeconds:0.00}s");
        }
        DonationStoryLifecycle.ResetForScene(scene.handle);

        EnsureRuntimeProbes();

        DonationGameplayState state;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            state = Blocked("SCENE_NOT_LOADED", $"scene='{sceneName}', valid={scene.IsValid()}, loaded={scene.isLoaded}", pendingDonations);
        }
        else if (PlayerFarming.Instance == null || DataManager.Instance == null)
        {
            state = Blocked("GAME_NOT_READY", $"scene='{sceneName}', player={PlayerFarming.Instance != null}, data={DataManager.Instance != null}", pendingDonations);
        }
        else if ((object)PlayerFarming.Instance is Behaviour playerBehaviour && !playerBehaviour.isActiveAndEnabled)
        {
            state = Blocked("PLAYER_CONTROL_DISABLED", $"scene='{sceneName}', PlayerFarming.activeAndEnabled=false", pendingDonations);
        }
        else if (now < _sceneStableAfter)
        {
            state = Blocked("SCENE_STABILIZING", $"scene='{sceneName}', remaining={Math.Max(0f, _sceneStableAfter - now):0.00}s", pendingDonations);
        }
        else if (Time.timeScale <= 0.001f)
        {
            state = Blocked("GAMEPLAY_PAUSED", $"scene='{sceneName}', timeScale={Time.timeScale:0.###}", pendingDonations);
        }
        else if (DonationStoryLifecycle.IsActive(out var storyEvidence))
        {
            state = Blocked("STORY_OR_DIALOGUE", storyEvidence, pendingDonations);
        }
        else if (TryReadRuntimeBlock(out var reason, out var evidence))
        {
            state = Blocked(reason, evidence, pendingDonations);
        }
        else
        {
            var area = DungeonContext.GetArea(out var areaEvidence);
            // Once the game is ready every non-dungeon location intentionally uses base rules.
            if (!string.Equals(area, "DUNGEON", StringComparison.Ordinal)) area = "BASE";
            state = new DonationGameplayState(true, false, "READY", area, areaEvidence, pendingDonations, _revision);
        }

        LogTransition(state);
        return state;
    }

    private DonationGameplayState Blocked(string reason, string evidence, int pendingDonations)
        => new(false, true, reason, "UNKNOWN", evidence, pendingDonations, _revision);

    private void LogTransition(DonationGameplayState state)
    {
        var signature = $"{state.IsReady}|{state.Reason}|{state.Area}";
        if (string.Equals(signature, _lastLoggedSignature, StringComparison.Ordinal)) return;
        _lastLoggedSignature = signature;
        _revision++;
        state.Revision = _revision;
        _log.LogInfo($"[DONATION][GATE][STATE] ready={state.IsReady}, timersPaused={state.TimersPaused}, reason={state.Reason}, area={state.Area}, pending={state.PendingDonations}, revision={state.Revision}, evidence={state.Evidence}");
    }

    private bool TryReadRuntimeBlock(out string reason, out string evidence)
    {
        foreach (var probe in _runtimeProbes)
        {
            try
            {
                if (probe.Read() != true) continue;
                reason = probe.Category;
                evidence = probe.Description;
                return true;
            }
            catch
            {
                // A game update may remove a reflected singleton/member. Other probes and
                // scene/timeScale readiness checks remain valid, so never break the runtime pump.
            }
        }

        reason = string.Empty;
        evidence = string.Empty;
        return false;
    }

    private void EnsureRuntimeProbes()
    {
        if (_probesBuilt) return;
        _probesBuilt = true;

        Type[] types;
        try { types = typeof(PlayerFarming).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).Cast<Type>().ToArray(); }

        foreach (var type in types)
        {
            var category = ClassifyType(type.Name);
            if (category == null) continue;

            foreach (var property in type.GetProperties(AnyStatic))
            {
                if (property.PropertyType != typeof(bool) || property.GetIndexParameters().Length != 0 || !IsBlockingMember(property.Name)) continue;
                var getter = property.GetGetMethod(true);
                if (getter == null || !getter.IsStatic) continue;
                _runtimeProbes.Add(new BooleanProbe(category, $"{type.FullName}.{property.Name}=true", () => (bool?)property.GetValue(null, null)));
            }

            foreach (var field in type.GetFields(AnyStatic))
            {
                if (field.FieldType != typeof(bool) || !IsBlockingMember(field.Name)) continue;
                _runtimeProbes.Add(new BooleanProbe(category, $"{type.FullName}.{field.Name}=true", () => (bool?)field.GetValue(null)));
            }

            var instanceGetter = BuildSingletonGetter(type);
            if (instanceGetter == null) continue;

            foreach (var property in type.GetProperties(AnyInstance))
            {
                if (property.PropertyType != typeof(bool) || property.GetIndexParameters().Length != 0 || !IsBlockingMember(property.Name)) continue;
                var getter = property.GetGetMethod(true);
                if (getter == null || getter.IsStatic) continue;
                _runtimeProbes.Add(new BooleanProbe(category, $"{type.FullName}.Instance.{property.Name}=true", () =>
                {
                    var instance = instanceGetter();
                    return instance == null ? null : (bool?)property.GetValue(instance, null);
                }));
            }

            foreach (var field in type.GetFields(AnyInstance))
            {
                if (field.FieldType != typeof(bool) || !IsBlockingMember(field.Name)) continue;
                _runtimeProbes.Add(new BooleanProbe(category, $"{type.FullName}.Instance.{field.Name}=true", () =>
                {
                    var instance = instanceGetter();
                    return instance == null ? null : (bool?)field.GetValue(instance);
                }));
            }
        }

        // PlayerFarming does not have a dialogue-like type name, but newer game builds may
        // expose an explicit interaction/input lock on it. Probe only unambiguous lock members;
        // generic CanMove/CanAttack flags would incorrectly pause during ordinary combat.
        var playerGetter = BuildSingletonGetter(typeof(PlayerFarming));
        if (playerGetter != null)
        {
            foreach (var property in typeof(PlayerFarming).GetProperties(AnyInstance))
            {
                if (property.PropertyType != typeof(bool) || property.GetIndexParameters().Length != 0 || !IsPlayerBlockingMember(property.Name)) continue;
                var getter = property.GetGetMethod(true);
                if (getter == null || getter.IsStatic) continue;
                _runtimeProbes.Add(new BooleanProbe("PLAYER_CONTROL_LOCKED", $"PlayerFarming.Instance.{property.Name}=true", () =>
                {
                    var instance = playerGetter();
                    return instance == null ? null : (bool?)property.GetValue(instance, null);
                }));
            }
            foreach (var field in typeof(PlayerFarming).GetFields(AnyInstance))
            {
                if (field.FieldType != typeof(bool) || !IsPlayerBlockingMember(field.Name)) continue;
                _runtimeProbes.Add(new BooleanProbe("PLAYER_CONTROL_LOCKED", $"PlayerFarming.Instance.{field.Name}=true", () =>
                {
                    var instance = playerGetter();
                    return instance == null ? null : (bool?)field.GetValue(instance);
                }));
            }
        }

        var descriptions = string.Join(", ", _runtimeProbes.Take(20).Select(x => x.Description));
        _log.LogInfo($"[DONATION][GATE][CAPABILITY] cachedRuntimeProbes={_runtimeProbes.Count}; probes=[{descriptions}{(_runtimeProbes.Count > 20 ? ", ..." : string.Empty)}]");
    }

    private static Func<object?>? BuildSingletonGetter(Type type)
    {
        var property = type.GetProperties(AnyStatic).FirstOrDefault(x =>
            x.GetIndexParameters().Length == 0 &&
            (string.Equals(x.Name, "Instance", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, "instance", StringComparison.Ordinal)) &&
            type.IsAssignableFrom(x.PropertyType) && x.GetGetMethod(true)?.IsStatic == true);
        if (property != null) return () => property.GetValue(null, null);

        var field = type.GetFields(AnyStatic).FirstOrDefault(x =>
            (string.Equals(x.Name, "Instance", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, "instance", StringComparison.Ordinal)) &&
            type.IsAssignableFrom(x.FieldType));
        return field == null ? null : () => field.GetValue(null);
    }

    private static string? ClassifyType(string name)
    {
        if (ContainsAny(name, "Loading", "LoadScreen", "SceneTransition", "Transition")) return "LOADING_OR_TRANSITION";
        if (ContainsAny(name, "Dialogue", "Dialog", "Conversation", "Cutscene", "Cinematic", "Narrative", "Story")) return "STORY_OR_DIALOGUE";
        if (ContainsAny(name, "Interaction")) return "STORY_OR_INTERACTION";
        return null;
    }

    private static bool IsBlockingMember(string name) => EqualsAny(name,
        "IsLoading", "Loading", "IsTransitioning", "Transitioning", "IsInTransition", "InTransition", "IsSceneChanging", "SceneChanging",
        "IsInDialogue", "InDialogue", "IsDialogueActive", "DialogueActive", "IsPlayingDialogue", "DialoguePlaying",
        "IsTalking", "Talking", "IsInConversation", "InConversation", "IsConversationActive",
        "IsCutscenePlaying", "IsInCutscene", "InCutscene", "IsCinematicPlaying", "IsInCinematic", "InCinematic",
        "IsInteracting", "InInteraction", "IsPlaying", "Playing", "IsInProgress", "InProgress");

    private static bool IsPlayerBlockingMember(string name) => EqualsAny(name,
        "IsInDialogue", "InDialogue", "IsTalking", "Talking", "IsInConversation", "InConversation",
        "IsInCutscene", "InCutscene", "IsInteracting", "InInteraction",
        "IsInputLocked", "InputLocked", "IsControlLocked", "ControlLocked", "ControlsLocked");

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0);

    private static bool EqualsAny(string value, params string[] candidates)
        => candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

    private sealed class BooleanProbe
    {
        public BooleanProbe(string category, string description, Func<bool?> read)
        {
            Category = category;
            Description = description;
            Read = read;
        }

        public string Category { get; }
        public string Description { get; }
        public Func<bool?> Read { get; }
    }
}

internal sealed class DonationGameplayState
{
    public DonationGameplayState(bool isReady, bool timersPaused, string reason, string area, string evidence, int pendingDonations, long revision)
    {
        IsReady = isReady;
        TimersPaused = timersPaused;
        Reason = reason;
        Area = area;
        Evidence = evidence;
        PendingDonations = pendingDonations;
        Revision = revision;
    }

    public bool IsReady { get; }
    public bool TimersPaused { get; }
    public string Reason { get; }
    public string Area { get; }
    public string Evidence { get; }
    public int PendingDonations { get; set; }
    public long Revision { get; set; }
}
