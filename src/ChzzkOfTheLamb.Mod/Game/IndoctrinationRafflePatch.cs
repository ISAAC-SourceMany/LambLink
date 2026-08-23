using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ChzzkOfTheLamb.Mod.Game;

// RC11: prefer the actual recruit interaction lifecycle over UI visibility.
// The primary hook is FollowerRecruit.ContinueRecruit / ContinueRecruitRoutine,
// which are part of the vanilla recruit interaction sequence. UI hooks remain
// only as secondary compatibility fallbacks.
internal static class IndoctrinationRafflePatch
{
    private static bool _installed;

    internal static void Install(Harmony harmony)
    {
        if (_installed) return;
        _installed = true;

        var installed = 0;
        var primaryInstalled = 0;

        // PRIMARY: hook the vanilla recruit interaction lifecycle itself.
        try
        {
            var followerRecruitType = AccessTools.TypeByName("FollowerRecruit");
            if (followerRecruitType == null)
            {
                Plugin.LogRafflePatchDiagnostic("[PRIMARY] FollowerRecruit type not found.");
            }
            else
            {
                var interactionTargets = AccessTools.GetDeclaredMethods(followerRecruitType)
                    .Where(m => string.Equals(m.Name, "ContinueRecruit", StringComparison.Ordinal)
                             || string.Equals(m.Name, "ContinueRecruitRoutine", StringComparison.Ordinal))
                    .ToArray();

                Plugin.LogRafflePatchDiagnostic(interactionTargets.Length == 0
                    ? "[PRIMARY] no FollowerRecruit.ContinueRecruit/ContinueRecruitRoutine targets found."
                    : $"[PRIMARY] discovered {interactionTargets.Length} recruit interaction target(s): {string.Join(" | ", interactionTargets.Select(Describe))}");

                var prefix = new HarmonyMethod(typeof(IndoctrinationRafflePatch), nameof(FollowerRecruitInteractionPrefix));
                foreach (var target in interactionTargets)
                {
                    harmony.Patch(target, prefix: prefix);
                    installed++;
                    primaryInstalled++;
                    Plugin.LogRafflePatchDiagnostic($"[PRIMARY] installed recruit interaction hook: {Describe(target)}");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogRafflePatchDiagnostic($"[PRIMARY] recruit interaction hook install failed: {ex.GetBaseException().Message}");
        }

        // SECONDARY: known UIManager wrapper. Kept for compatibility only.
        try
        {
            var uiManager = AccessTools.TypeByName("Lamb.UI.UIManager");
            if (uiManager == null)
            {
                Plugin.LogRafflePatchDiagnostic("[SECONDARY] UIManager type not found; ShowIndoctrinationMenu hook unavailable.");
            }
            else
            {
                var targets = AccessTools.GetDeclaredMethods(uiManager)
                    .Where(m => string.Equals(m.Name, "ShowIndoctrinationMenu", StringComparison.Ordinal))
                    .ToArray();

                Plugin.LogRafflePatchDiagnostic(targets.Length == 0
                    ? "[SECONDARY] ShowIndoctrinationMenu target not found."
                    : $"[SECONDARY] discovered {targets.Length} ShowIndoctrinationMenu overload(s): {string.Join(" | ", targets.Select(Describe))}");

                var prefix = new HarmonyMethod(typeof(IndoctrinationRafflePatch), nameof(UIManagerPrefix));
                foreach (var target in targets)
                {
                    harmony.Patch(target, prefix: prefix);
                    installed++;
                    Plugin.LogRafflePatchDiagnostic($"[SECONDARY] installed UIManager hook: {Describe(target)}");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogRafflePatchDiagnostic($"[SECONDARY] UIManager hook install failed: {ex.GetBaseException().Message}");
        }

        // SECONDARY: appearance form controller. Also compatibility-only.
        try
        {
            var formType = AccessTools.TypeByName("Lamb.UI.UIAppearanceMenuController_Form");
            if (formType == null)
            {
                Plugin.LogRafflePatchDiagnostic("[SECONDARY] UIAppearanceMenuController_Form type not found.");
            }
            else
            {
                var onShowStarted = AccessTools.Method(formType, "OnShowStarted", Type.EmptyTypes);
                if (onShowStarted == null)
                {
                    Plugin.LogRafflePatchDiagnostic("[SECONDARY] UIAppearanceMenuController_Form.OnShowStarted() not found.");
                }
                else
                {
                    var postfix = new HarmonyMethod(typeof(IndoctrinationRafflePatch), nameof(FormControllerPostfix));
                    harmony.Patch(onShowStarted, postfix: postfix);
                    installed++;
                    Plugin.LogRafflePatchDiagnostic($"[SECONDARY] installed form-controller hook: {Describe(onShowStarted)}");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogRafflePatchDiagnostic($"[SECONDARY] form-controller hook install failed: {ex.GetBaseException().Message}");
        }

        Plugin.LogRafflePatchDiagnostic($"installation complete: primary={primaryInstalled}, total={installed}. Raffle trigger priority=FollowerRecruit lifecycle > UI fallbacks.");
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

    private static void FollowerRecruitInteractionPrefix(object __instance, object[]? __args, MethodBase __originalMethod)
    {
        var args = (__args ?? Array.Empty<object>()).ToList();
        if (__instance != null) args.Insert(0, __instance);
        Plugin.NotifyRecruitInteractionStarted(args.ToArray(), __originalMethod?.Name ?? "FollowerRecruit");
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
