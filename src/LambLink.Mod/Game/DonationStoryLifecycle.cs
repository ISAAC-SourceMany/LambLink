using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace LambLink.Mod.Game;

internal static class DonationStoryLifecycle
{
    private static readonly HashSet<string> BeginNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BeginDialogue", "StartDialogue", "ShowDialogue", "PlayDialogue", "OpenDialogue",
        "BeginConversation", "StartConversation", "ShowConversation", "PlayConversation",
        "BeginCutscene", "StartCutscene", "PlayCutscene", "ShowCutscene",
        "BeginCinematic", "StartCinematic", "PlayCinematic",
        "BeginNarrative", "StartNarrative", "PlayNarrative",
        "BeginStory", "StartStory", "ShowStory", "PlayStory", "OpenStory",
        // Generic lifecycle names are accepted only on a dialogue/cutscene-named type that
        // also declares a matching generic end method (verified below).
        "Begin", "Show", "Open", "Play"
    };

    private static readonly HashSet<string> EndNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "EndDialogue", "StopDialogue", "CloseDialogue", "HideDialogue", "CompleteDialogue", "FinishDialogue",
        "EndConversation", "StopConversation", "CloseConversation", "CompleteConversation", "FinishConversation",
        "EndCutscene", "StopCutscene", "CloseCutscene", "CompleteCutscene", "FinishCutscene",
        "EndCinematic", "StopCinematic", "CompleteCinematic", "FinishCinematic",
        "EndNarrative", "StopNarrative", "CompleteNarrative", "FinishNarrative",
        "EndStory", "StopStory", "CloseStory", "HideStory", "CompleteStory", "FinishStory",
        "End", "Hide", "Close", "Stop", "Finish", "Complete"
    };

    private static ManualLogSource? _log;
    private static bool _active;
    private static string _evidence = string.Empty;
    private static int _sceneHandle = int.MinValue;

    public static void Install(Harmony harmony, ManualLogSource log)
    {
        _log = log;
        var beginPatch = new HarmonyMethod(typeof(DonationStoryLifecycle), nameof(BeginPrefix));
        var endPatch = new HarmonyMethod(typeof(DonationStoryLifecycle), nameof(EndPostfix));
        var beginCount = 0;
        var endCount = 0;

        Type[] types;
        try { types = typeof(PlayerFarming).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).Cast<Type>().ToArray(); }

        foreach (var type in types.Where(IsStoryType))
        {
            var declaredMethods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.DeclaringType == type && !method.IsAbstract && !method.IsGenericMethodDefinition)
                .ToArray();
            var beginMethods = declaredMethods.Where(method => BeginNames.Contains(method.Name)).ToArray();
            var endMethods = declaredMethods.Where(method => EndNames.Contains(method.Name)).ToArray();
            // A begin-only guess could pause donations forever. Install lifecycle hooks only
            // where the same controller exposes a verifiable matching end operation.
            if (beginMethods.Length == 0 || endMethods.Length == 0) continue;

            foreach (var method in beginMethods)
            {
                try
                {
                    harmony.Patch(method, prefix: beginPatch);
                    beginCount++;
                }
                catch (Exception ex)
                {
                    log.LogWarning($"[DONATION][STORY-HOOK][SKIPPED] target={type.FullName}.{method.Name}: {ex.GetBaseException().Message}");
                }
            }

            foreach (var method in endMethods)
            {
                try
                {
                    harmony.Patch(method, postfix: endPatch);
                    endCount++;
                }
                catch (Exception ex)
                {
                    log.LogWarning($"[DONATION][STORY-HOOK][SKIPPED] target={type.FullName}.{method.Name}: {ex.GetBaseException().Message}");
                }
            }
        }

        log.LogInfo($"[DONATION][STORY-HOOK][CAPABILITY] beginTargets={beginCount}, endTargets={endCount}; boolean runtime probes and timeScale remain active fallbacks");
    }

    public static bool IsActive(out string evidence)
    {
        evidence = _evidence;
        return _active;
    }

    public static void ResetForScene(int sceneHandle)
    {
        if (_sceneHandle == sceneHandle) return;
        _sceneHandle = sceneHandle;
        if (_active)
            _log?.LogInfo($"[DONATION][STORY-HOOK][RESET] sceneHandle={sceneHandle}, previous={_evidence}");
        _active = false;
        _evidence = string.Empty;
    }

    private static void BeginPrefix(MethodBase __originalMethod)
    {
        _active = true;
        _evidence = Format(__originalMethod);
        _log?.LogInfo($"[DONATION][STORY-HOOK][BEGIN] target={_evidence}");
    }

    private static void EndPostfix(MethodBase __originalMethod)
    {
        var endedBy = Format(__originalMethod);
        _active = false;
        _evidence = string.Empty;
        _log?.LogInfo($"[DONATION][STORY-HOOK][END] target={endedBy}");
    }

    private static bool IsStoryType(Type type)
    {
        var name = type.Name;
        return name.IndexOf("Dialogue", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Conversation", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Cutscene", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Cinematic", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Narrative", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("Story", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Format(MethodBase method)
        => $"{method.DeclaringType?.FullName}.{method.Name}";
}
