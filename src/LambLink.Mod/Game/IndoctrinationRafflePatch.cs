using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace LambLink.Mod.Game;

// Production raffle trigger: do not open the raffle merely because a recruit exists.
// Start it when COTL actually opens the indoctrination menu for that recruit.
[HarmonyPatch]
internal static class IndoctrinationRafflePatch
{
    private static MethodBase[] _resolvedTargets = Array.Empty<MethodBase>();

    private static IEnumerable<MethodBase> TargetMethods()
    {
        var uiManager = AccessTools.TypeByName("Lamb.UI.UIManager");
        if (uiManager == null)
        {
            Plugin.LogRafflePatchTarget(null);
            return Array.Empty<MethodBase>();
        }
        var targets = AccessTools.GetDeclaredMethods(uiManager)
            .Where(m => string.Equals(m.Name, "ShowIndoctrinationMenu", StringComparison.Ordinal))
            .Cast<MethodBase>()
            .ToArray();
        _resolvedTargets = targets;
        if (targets.Length == 0) Plugin.LogRafflePatchTarget(null);
        else foreach (var target in targets) Plugin.LogRafflePatchTarget(target);
        return targets;
    }

    internal static void VerifyInstallation(string ownerId)
    {
        foreach (var target in _resolvedTargets)
        {
            var patchInfo = Harmony.GetPatchInfo(target);
            var owners = patchInfo?.Prefixes.Select(x => x.owner).Distinct().ToArray() ?? Array.Empty<string>();
            Plugin.LogRafflePatchVerification(target, owners.Contains(ownerId), string.Join(",", owners));
        }
    }

    private static void Prefix(object[] __args)
    {
        Plugin.NotifyIndoctrinationMenuOpened(__args ?? Array.Empty<object>());
    }
}
