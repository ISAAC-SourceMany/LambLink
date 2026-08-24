using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ChzzkOfTheLamb.Mod.Game;

// COTL's normal village follower-name UI is UIFollowerName. We patch its final SetText step.
// RC5 keeps the vanilla name label untouched and adds a separate green CHZZK TMP badge next to it.
// FollowerInfo.Name remains the plain viewer nickname and COTL may refresh its own text freely.
[HarmonyPatch]
internal static class FollowerNameplatePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var type = AccessTools.TypeByName("UIFollowerName");
        if (type == null) return Array.Empty<MethodBase>();
        return AccessTools.GetDeclaredMethods(type)
            .Where(m => string.Equals(m.Name, "SetText", StringComparison.Ordinal))
            .Cast<MethodBase>()
            .ToArray();
    }

    private static void Postfix(object __instance)
    {
        Plugin.RefreshFollowerNameplate(__instance);
    }

    internal static void VerifyInstallation(string owner, ManualLogSource log)
    {
        var targets = TargetMethods().ToArray();
        if (targets.Length == 0)
        {
            log.LogError("[NAMEPLATE][PATCH-VERIFY] UIFollowerName.SetText target count=0");
            return;
        }

        foreach (var target in targets)
        {
            var installed = Harmony.GetPatchInfo(target)?.Postfixes.Any(x => x.owner == owner) == true;
            var parameters = string.Join(",", target.GetParameters().Select(x => x.ParameterType.Name));
            log.LogInfo($"[NAMEPLATE][PATCH-VERIFY] target={target.DeclaringType?.FullName}.{target.Name}({parameters}), installed={installed}");
        }
    }
}
