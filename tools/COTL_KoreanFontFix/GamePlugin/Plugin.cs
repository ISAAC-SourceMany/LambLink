using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace COTLKoreanFontFix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "kr.ruon.cotl.koreanfontfix";
        public const string PluginName = "COTL Korean Font Fix";
        public const string PluginVersion = "4.3.0";

        private const string BundleFileName = "koreanfont.bundle";
        private const string FontAssetName = "KoreanRuntimeFont";

        internal static Plugin Instance;

        private Harmony _harmony;
        private AssetBundle _bundle;
        private TMP_FontAsset _koreanFont;

        private ConfigEntry<bool> _verbose;
        private ConfigEntry<bool> _normalizeNfc;

        private readonly HashSet<int> _patchedFontIds = new HashSet<int>();

        private void Awake()
        {
            Instance = this;

            _verbose = Config.Bind(
                "Debug",
                "VerboseLog",
                false,
                "Log Unicode normalization and Korean fallback routing.");

            _normalizeNfc = Config.Bind(
                "Text",
                "NormalizeHangulToNFC",
                true,
                "Normalize Korean text to Unicode NFC immediately before TMP renders it.");

            if (!LoadBundledFont())
            {
                Logger.LogError(
                    "Korean font bundle is unavailable. " +
                    "Build koreanfont.bundle with build-font-bundle.ps1 and place it beside this DLL.");
            }

            InstallHarmonyPatches();

            Logger.LogInfo(
                $"{PluginName} {PluginVersion} loaded. " +
                $"RuntimeFont={(_koreanFont != null ? _koreanFont.name : "(none)")}, " +
                "routing=fallback-only");
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }

            if (_bundle != null)
            {
                try { _bundle.Unload(false); } catch { }
            }

            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        private bool LoadBundledFont()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string bundlePath = Path.Combine(pluginDir, BundleFileName);

                Logger.LogInfo("Looking for Korean font bundle: " + bundlePath);

                if (!File.Exists(bundlePath))
                {
                    Logger.LogError("Font bundle not found: " + bundlePath);
                    return false;
                }

                _bundle = AssetBundle.LoadFromFile(bundlePath);
                if (_bundle == null)
                {
                    Logger.LogError("AssetBundle.LoadFromFile returned null.");
                    return false;
                }

                _koreanFont = _bundle.LoadAsset<TMP_FontAsset>(FontAssetName);

                if (_koreanFont == null)
                {
                    TMP_FontAsset[] all = _bundle.LoadAllAssets<TMP_FontAsset>();
                    if (all != null && all.Length > 0)
                        _koreanFont = all[0];
                }

                if (_koreanFont == null)
                {
                    Logger.LogError("No TMP_FontAsset exists in koreanfont.bundle.");
                    return false;
                }

                _koreanFont.name = FontAssetName;

                // The release bundle is pre-baked in the Unity editor.
                // Never allow runtime atlas population.
                _koreanFont.atlasPopulationMode = AtlasPopulationMode.Static;

                DontDestroyOnLoad(_koreanFont);

                if (_koreanFont.material != null)
                    DontDestroyOnLoad(_koreanFont.material);

                Texture2D[] atlases = _koreanFont.atlasTextures;
                if (atlases != null)
                {
                    foreach (Texture2D atlas in atlases)
                    {
                        if (atlas != null)
                            DontDestroyOnLoad(atlas);
                    }
                }

                TryRegisterGlobalFallback();

                Logger.LogInfo(
                    $"Loaded bundled Korean TMP font: {_koreanFont.name}, " +
                    $"dynamic={_koreanFont.atlasPopulationMode}, " +
                    $"multiAtlas={_koreanFont.isMultiAtlasTexturesEnabled}, " +
                    $"atlas={_koreanFont.atlasWidth}x{_koreanFont.atlasHeight}, " +
                    $"atlasCount={(atlases != null ? atlases.Length : 0)}, " +
                    $"sourceFont={(_koreanFont.sourceFontFile != null ? _koreanFont.sourceFontFile.name : "(null)")}");

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to load bundled Korean font: " + ex);
                return false;
            }
        }

        private void TryRegisterGlobalFallback()
        {
            if (_koreanFont == null)
                return;

            try
            {
                List<TMP_FontAsset> fallbacks = TMP_Settings.fallbackFontAssets;

                if (fallbacks != null && !fallbacks.Contains(_koreanFont))
                {
                    fallbacks.Insert(0, _koreanFont);
                    Logger.LogInfo("Registered KoreanRuntimeFont in TMP global fallback list.");
                }
                else if (fallbacks == null)
                {
                    Logger.LogWarning(
                        "TMP_Settings.fallbackFontAssets is null. " +
                        "Per-font fallback attachment will still be used.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Global TMP fallback registration failed: " + ex.Message);
            }
        }

        private void InstallHarmonyPatches()
        {
            _harmony = new Harmony(PluginGuid);

            MethodInfo textPrefix = AccessTools.Method(
                typeof(Plugin),
                nameof(TmpTextStringPrefix));

            MethodInfo inputPrefix = AccessTools.Method(
                typeof(Plugin),
                nameof(InputFieldUpdatePrefix));

            MethodInfo textSetter = AccessTools.PropertySetter(
                typeof(TMP_Text),
                "text");

            if (textSetter != null)
            {
                _harmony.Patch(
                    textSetter,
                    prefix: new HarmonyMethod(textPrefix));
            }

            foreach (MethodInfo method in typeof(TMP_Text).GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic))
            {
                if (method.Name != "SetText")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 ||
                    parameters[0].ParameterType != typeof(string))
                {
                    continue;
                }

                try
                {
                    _harmony.Patch(
                        method,
                        prefix: new HarmonyMethod(textPrefix));
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        $"Could not patch {method}: {ex.Message}");
                }
            }

            MethodInfo updateLabel = AccessTools.Method(
                typeof(TMP_InputField),
                "UpdateLabel");

            if (updateLabel != null)
            {
                _harmony.Patch(
                    updateLabel,
                    prefix: new HarmonyMethod(inputPrefix));
            }

            Logger.LogInfo("TMP_Text and TMP_InputField Harmony patches installed.");
        }

        public static void TmpTextStringPrefix(
            TMP_Text __instance,
            ref string __0)
        {
            Plugin plugin = Instance;

            if (plugin == null ||
                __instance == null ||
                String.IsNullOrEmpty(__0))
            {
                return;
            }

            if (!ContainsHangulOrJamo(__0))
                return;

            try
            {
                string value = __0;

                if (plugin._normalizeNfc.Value)
                {
                    string normalized =
                        value.Normalize(NormalizationForm.FormC);

                    if (plugin._verbose.Value &&
                        !String.Equals(
                            value,
                            normalized,
                            StringComparison.Ordinal))
                    {
                        plugin.Logger.LogInfo(
                            "NFC: " +
                            ToCodePoints(value) +
                            " -> " +
                            ToCodePoints(normalized));
                    }

                    value = normalized;
                    __0 = normalized;
                }

                plugin.RouteTextToKoreanFont(
                    __instance,
                    value);
            }
            catch (Exception ex)
            {
                plugin.Logger.LogWarning(
                    "TMP Korean text routing failed: " +
                    ex.Message);
            }
        }

        public static void InputFieldUpdatePrefix(
            TMP_InputField __instance)
        {
            Plugin plugin = Instance;

            if (plugin == null ||
                __instance == null ||
                plugin._koreanFont == null)
            {
                return;
            }

            try
            {
                string committed =
                    __instance.text ?? String.Empty;

                string composition =
                    Input.compositionString ?? String.Empty;

                string display =
                    committed + composition;

                if (!ContainsHangulOrJamo(display))
                    return;

                string normalized =
                    plugin._normalizeNfc.Value
                        ? display.Normalize(NormalizationForm.FormC)
                        : display;

                TMP_Text textComponent =
                    __instance.textComponent;

                if (textComponent != null)
                {
                    plugin.RouteTextToKoreanFont(
                        textComponent,
                        normalized);
                }
            }
            catch (Exception ex)
            {
                if (plugin._verbose.Value)
                {
                    plugin.Logger.LogWarning(
                        "TMP_InputField fallback routing failed: " +
                        ex.Message);
                }
            }
        }

        private void RouteTextToKoreanFont(
            TMP_Text text,
            string value)
        {
            if (_koreanFont == null ||
                text == null ||
                String.IsNullOrEmpty(value))
            {
                return;
            }

            if (!ContainsHangulOrJamo(value))
                return;

            // Release architecture:
            // - preserve the text object's original primary font
            // - add KoreanRuntimeFont only as a fallback
            // This avoids TMP_SubMeshUI/material lifecycle issues.
            AttachFallback(text.font);
        }

        private void AttachFallback(
            TMP_FontAsset baseFont)
        {
            if (_koreanFont == null ||
                baseFont == null ||
                baseFont == _koreanFont)
            {
                return;
            }

            int id = baseFont.GetInstanceID();

            if (_patchedFontIds.Contains(id))
                return;

            try
            {
                List<TMP_FontAsset> table =
                    baseFont.fallbackFontAssetTable;

                if (table == null)
                {
                    table = new List<TMP_FontAsset>();
                    baseFont.fallbackFontAssetTable = table;
                }

                if (!table.Contains(_koreanFont))
                    table.Insert(0, _koreanFont);

                _patchedFontIds.Add(id);

                if (_verbose.Value)
                {
                    Logger.LogInfo(
                        "Attached Korean fallback to: " +
                        baseFont.name);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    $"Could not attach Korean fallback to '{baseFont.name}': " +
                    ex.Message);
            }
        }

        private static bool ContainsHangulOrJamo(
            string value)
        {
            foreach (char c in value)
            {
                if (IsHangulOrJamo(c))
                    return true;
            }

            return false;
        }

        private static bool IsHangulOrJamo(
            char c)
        {
            int u = c;

            if (u >= 0x1100 && u <= 0x11FF) return true;
            if (u >= 0x3130 && u <= 0x318F) return true;
            if (u >= 0xA960 && u <= 0xA97F) return true;
            if (u >= 0xAC00 && u <= 0xD7A3) return true;
            if (u >= 0xD7B0 && u <= 0xD7FF) return true;

            return false;
        }

        private static string ToCodePoints(
            string value)
        {
            if (String.IsNullOrEmpty(value))
                return "(none)";

            var sb = new StringBuilder();

            for (int i = 0; i < value.Length; i++)
            {
                if (i != 0)
                    sb.Append(' ');

                sb.Append("U+");
                sb.Append(((int)value[i]).ToString("X4"));
            }

            return sb.ToString();
        }
    }
}
