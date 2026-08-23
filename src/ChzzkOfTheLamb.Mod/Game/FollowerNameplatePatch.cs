using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ChzzkOfTheLamb.Mod.Game;

// COTL's normal village follower-name UI is UIFollowerName. We patch its final SetText step.
// RC5 keeps the vanilla name label untouched and adds a separate green CHZZK TMP badge next to it.
// FollowerInfo.Name remains the plain viewer nickname and COTL may refresh its own text freely.
[HarmonyPatch]
internal static class FollowerNameplatePatch
{
    private static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("UIFollowerName");
        if (type == null) return null;
        return AccessTools.GetDeclaredMethods(type)
            .FirstOrDefault(m => string.Equals(m.Name, "SetText", StringComparison.Ordinal));
    }

    private static void Postfix(object __instance)
    {
        Plugin.RefreshFollowerNameplate(__instance);
    }
}
