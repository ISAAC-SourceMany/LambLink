using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ChzzkOfTheLamb.Mod.Game;

// Production raffle trigger: do not open the raffle merely because a recruit exists.
// Start it when COTL actually opens the indoctrination menu for that recruit.
[HarmonyPatch]
internal static class IndoctrinationRafflePatch
{
    private static MethodBase? TargetMethod()
    {
        var uiManager = AccessTools.TypeByName("Lamb.UI.UIManager");
        if (uiManager == null) return null;
        return AccessTools.GetDeclaredMethods(uiManager)
            .FirstOrDefault(m => string.Equals(m.Name, "ShowIndoctrinationMenu", StringComparison.Ordinal));
    }

    private static void Prefix(object[] __args)
    {
        Plugin.NotifyIndoctrinationMenuOpened(__args ?? Array.Empty<object>());
    }
}
