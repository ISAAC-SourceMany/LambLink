using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ChzzkOfTheLamb.Mod.Game;

// Production raffle trigger: do not open the raffle merely because a recruit exists.
// Patch every UIManager.ShowIndoctrinationMenu overload. Some COTL builds route the
// real UI call through an overload that is not the first method returned by reflection.
[HarmonyPatch]
internal static class IndoctrinationRafflePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var uiManager = AccessTools.TypeByName("Lamb.UI.UIManager");
        if (uiManager == null)
        {
            Plugin.LogRafflePatchDiagnostic("UIManager type not found; Harmony trigger unavailable. UI-presence fallback remains enabled.");
            return Array.Empty<MethodBase>();
        }

        var targets = AccessTools.GetDeclaredMethods(uiManager)
            .Where(m => string.Equals(m.Name, "ShowIndoctrinationMenu", StringComparison.Ordinal))
            .Cast<MethodBase>()
            .ToArray();

        Plugin.LogRafflePatchDiagnostic(targets.Length == 0
            ? "ShowIndoctrinationMenu target not found; UI-presence fallback remains enabled."
            : $"patching {targets.Length} ShowIndoctrinationMenu overload(s): {string.Join(" | ", targets.Select(Describe))}");
        return targets;
    }

    private static string Describe(MethodBase method)
    {
        try
        {
            return $"{method.DeclaringType?.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name))})";
        }
        catch
        {
            return method.Name;
        }
    }

    private static void Prefix(object[]? __args)
    {
        Plugin.NotifyIndoctrinationMenuOpened(__args ?? Array.Empty<object>());
    }
}
