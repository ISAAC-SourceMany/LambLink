using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ChzzkOfTheLamb.Mod.Game;

// Explicitly installed raffle hooks. We intentionally do not rely on Harmony's assembly scan
// for these runtime-resolved COTL types, because some release builds loaded the plugin without
// ever invoking TargetMethods(). This installer logs every discovered/installed hook.
internal static class IndoctrinationRafflePatch
{
    private static bool _installed;

    internal static void Install(Harmony harmony)
    {
        if (_installed) return;
        _installed = true;

        var installed = 0;

        try
        {
            var uiManager = AccessTools.TypeByName("Lamb.UI.UIManager");
            if (uiManager == null)
            {
                Plugin.LogRafflePatchDiagnostic("UIManager type not found; ShowIndoctrinationMenu hook unavailable.");
            }
            else
            {
                var targets = AccessTools.GetDeclaredMethods(uiManager)
                    .Where(m => string.Equals(m.Name, "ShowIndoctrinationMenu", StringComparison.Ordinal))
                    .ToArray();

                Plugin.LogRafflePatchDiagnostic(targets.Length == 0
                    ? "ShowIndoctrinationMenu target not found."
                    : $"discovered {targets.Length} ShowIndoctrinationMenu overload(s): {string.Join(" | ", targets.Select(Describe))}");

                var prefix = new HarmonyMethod(typeof(IndoctrinationRafflePatch), nameof(UIManagerPrefix));
                foreach (var target in targets)
                {
                    harmony.Patch(target, prefix: prefix);
                    installed++;
                    Plugin.LogRafflePatchDiagnostic($"installed UIManager hook: {Describe(target)}");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogRafflePatchDiagnostic($"UIManager hook install failed: {ex.GetBaseException().Message}");
        }

        // This controller is present on the actual indoctrination appearance screen. OnShowStarted
        // runs when the UI becomes visible and also gives us an instance that contains _follower,
        // making recruit resolution more reliable than an empty UIManager argument list.
        try
        {
            var formType = AccessTools.TypeByName("Lamb.UI.UIAppearanceMenuController_Form");
            if (formType == null)
            {
                Plugin.LogRafflePatchDiagnostic("UIAppearanceMenuController_Form type not found; controller hook unavailable.");
            }
            else
            {
                var onShowStarted = AccessTools.Method(formType, "OnShowStarted", Type.EmptyTypes);
                if (onShowStarted == null)
                {
                    Plugin.LogRafflePatchDiagnostic("UIAppearanceMenuController_Form.OnShowStarted() not found.");
                }
                else
                {
                    var postfix = new HarmonyMethod(typeof(IndoctrinationRafflePatch), nameof(FormControllerPostfix));
                    harmony.Patch(onShowStarted, postfix: postfix);
                    installed++;
                    Plugin.LogRafflePatchDiagnostic($"installed form-controller hook: {Describe(onShowStarted)}");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogRafflePatchDiagnostic($"form-controller hook install failed: {ex.GetBaseException().Message}");
        }

        Plugin.LogRafflePatchDiagnostic($"explicit raffle hook installation complete: installed={installed}; runtime controller/UI fallback enabled.");
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

    private static void UIManagerPrefix(object[]? __args)
    {
        Plugin.NotifyIndoctrinationMenuOpened(__args ?? Array.Empty<object>());
    }

    private static void FormControllerPostfix(object __instance)
    {
        Plugin.NotifyIndoctrinationMenuOpened(__instance == null ? Array.Empty<object>() : new[] { __instance });
    }
}
