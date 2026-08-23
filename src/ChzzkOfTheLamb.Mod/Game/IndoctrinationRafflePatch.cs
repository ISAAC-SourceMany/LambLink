using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ChzzkOfTheLamb.Mod.Game;

// RC13: restore the exact HarmonyX patch style that was used by the dev10z
// build where the indoctrination raffle trigger was proven to fire.
// Important: this class is discovered by Harmony.CreateAndPatchAll().
[HarmonyPatch]
internal static class IndoctrinationRafflePatch
{
    private static MethodBase? TargetMethod()
    {
        var uiManager = AccessTools.TypeByName("Lamb.UI.UIManager");
        if (uiManager == null)
        {
            Plugin.LogRafflePatchDiagnostic("[LEGACY-RESTORE] Lamb.UI.UIManager not found.");
            return null;
        }

        var target = AccessTools.GetDeclaredMethods(uiManager)
            .FirstOrDefault(m => string.Equals(m.Name, "ShowIndoctrinationMenu", StringComparison.Ordinal));

        Plugin.LogRafflePatchDiagnostic(target == null
            ? "[LEGACY-RESTORE] ShowIndoctrinationMenu target not found."
            : $"[LEGACY-RESTORE] automatic Harmony target={Describe(target)}");

        return target;
    }

    private static void Prefix(object[] __args)
    {
        var args = __args ?? Array.Empty<object>();
        Plugin.LogRafflePatchDiagnostic($"[LEGACY-RESTORE] ShowIndoctrinationMenu Prefix fired; args={args.Length}");
        Plugin.NotifyIndoctrinationMenuOpened(args);
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
}
