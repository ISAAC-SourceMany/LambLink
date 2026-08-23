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
        public const string PluginVersion = "4.2.1";

        private const string BundleFileName = "koreanfont.bundle";
        private const string FontAssetName = "KoreanRuntimeFont";
        private const string AtlasTooLargeMessage =
            "Required atlas size exceeds supported max (4096x4096)";

        internal static Plugin Instance;

        private Harmony _harmony;
        private AssetBundle _bundle;
        private TMP_FontAsset _koreanFont;

        private ConfigEntry<bool> _verbose;
        private ConfigEntry<bool> _normalizeNfc;
        private ConfigEntry<bool> _traceAtlasErrors;
        private ConfigEntry<int> _maxAtlasErrorTraces;

        private readonly HashSet<int> _patchedFontIds = new HashSet<int>();
        private int _atlasErrorTraceCount;

        [ThreadStatic]
        private static Stack<FontOperationFrame> _fontOperationStack;

        private sealed class FontOperationFrame
        {
            public TMP_FontAsset Font;
            public string Method;
            public string Arguments;
            public string CallerStack;
        }

        private void Awake()
        {
            Instance = this;

            _verbose = Config.Bind(
                "Debug",
                "VerboseLog",
                false,
                "Log Unicode normalization and Korean fallback routing.");

            _traceAtlasErrors = Config.Bind(
                "Debug",
                "TraceAtlasErrors",
                true,
                "Trace TMP font operations when Unity reports the 4096x4096 atlas-size error. " +
                "Disable this after the offending font has been identified.");

            _maxAtlasErrorTraces = Config.Bind(
                "Debug",
                "MaxAtlasErrorTraces",
                12,
                "Maximum number of detailed 4096 atlas diagnostics emitted per game run.");

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

            if (_traceAtlasErrors.Value)
            {
                Application.logMessageReceived += OnUnityLogMessage;
                Logger.LogInfo(
                    "TMP atlas error tracing enabled. " +
                    $"Up to {Math.Max(1, _maxAtlasErrorTraces.Value)} atlas errors will be diagnosed.");
            }

            Logger.LogInfo(
                $"{PluginName} {PluginVersion} loaded. " +
                $"RuntimeFont={(_koreanFont != null ? _koreanFont.name : "(none)")}, " +
                "routing=fallback-only");
        }

        private void OnDestroy()
        {
            try { Application.logMessageReceived -= OnUnityLogMessage; } catch { }
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
                typeof(Plugin), nameof(TmpTextStringPrefix));

            MethodInfo inputPrefix = AccessTools.Method(
                typeof(Plugin), nameof(InputFieldUpdatePrefix));

            MethodInfo textSetter = AccessTools.PropertySetter(typeof(TMP_Text), "text");
            if (textSetter != null)
                _harmony.Patch(textSetter, prefix: new HarmonyMethod(textPrefix));

            foreach (MethodInfo m in typeof(TMP_Text).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "SetText")
                    continue;

                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].ParameterType != typeof(string))
                    continue;

                try
                {
                    _harmony.Patch(m, prefix: new HarmonyMethod(textPrefix));
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Could not patch {m}: {ex.Message}");
                }
            }

            MethodInfo updateLabel = AccessTools.Method(typeof(TMP_InputField), "UpdateLabel");
            if (updateLabel != null)
                _harmony.Patch(updateLabel, prefix: new HarmonyMethod(inputPrefix));

            if (_traceAtlasErrors.Value)
                InstallFontOperationTracePatches();

            Logger.LogInfo("TMP_Text and TMP_InputField Harmony patches installed.");
        }

        private void InstallFontOperationTracePatches()
        {
            MethodInfo prefix = AccessTools.Method(
                typeof(Plugin), nameof(FontOperationTracePrefix));

            MethodInfo finalizer = AccessTools.Method(
                typeof(Plugin), nameof(FontOperationTraceFinalizer));

            int patched = 0;

            foreach (MethodInfo method in typeof(TMP_FontAsset).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "TryAddCharacters" &&
                    method.Name != "TryAddCharacter" &&
                    method.Name != "TryAddCharacterInternal")
                {
                    continue;
                }

                try
                {
                    _harmony.Patch(
                        method,
                        prefix: new HarmonyMethod(prefix),
                        finalizer: new HarmonyMethod(finalizer));
                    patched++;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        $"Could not install atlas trace on {method}: {ex.Message}");
                }
            }

            Logger.LogInfo(
                $"TMP atlas trace hooks installed on {patched} font-operation methods.");
        }

        public static void FontOperationTracePrefix(
            TMP_FontAsset __instance,
            MethodBase __originalMethod,
            object[] __args)
        {
            Plugin p = Instance;
            if (p == null || p._traceAtlasErrors == null || !p._traceAtlasErrors.Value)
                return;

            if (_fontOperationStack == null)
                _fontOperationStack = new Stack<FontOperationFrame>();

            _fontOperationStack.Push(new FontOperationFrame
            {
                Font = __instance,
                Method = DescribeMethod(__originalMethod),
                Arguments = DescribeArguments(__args),
                CallerStack = Environment.StackTrace
            });
        }

        public static Exception FontOperationTraceFinalizer(Exception __exception)
        {
            if (_fontOperationStack != null && _fontOperationStack.Count > 0)
                _fontOperationStack.Pop();

            return __exception;
        }

        private void OnUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            if (_traceAtlasErrors == null || !_traceAtlasErrors.Value)
                return;

            if (String.IsNullOrEmpty(condition) ||
                condition.IndexOf(AtlasTooLargeMessage, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            int max = Math.Max(1, _maxAtlasErrorTraces.Value);
            if (_atlasErrorTraceCount >= max)
                return;

            _atlasErrorTraceCount++;

            try
            {
                Logger.LogWarning(
                    $"[ATLAS-TRACE #{_atlasErrorTraceCount}] 4096 atlas limit error detected.");

                FontOperationFrame active = GetActiveFontOperation();
                if (active != null)
                {
                    Logger.LogWarning(
                        "[ATLAS-TRACE] Active TMP_FontAsset: " + DescribeFont(active.Font));
                    Logger.LogWarning(
                        "[ATLAS-TRACE] Active operation: " + active.Method);
                    Logger.LogWarning(
                        "[ATLAS-TRACE] Arguments: " + active.Arguments);

                    if (!String.IsNullOrWhiteSpace(active.CallerStack))
                    {
                        Logger.LogWarning(
                            "[ATLAS-TRACE] Caller stack captured at font operation:\n" +
                            TrimStack(active.CallerStack, 32));
                    }
                }
                else
                {
                    Logger.LogWarning(
                        "[ATLAS-TRACE] No active TMP_FontAsset TryAddCharacter(s) operation was captured.");
                }

                if (!String.IsNullOrWhiteSpace(stackTrace))
                {
                    Logger.LogWarning(
                        "[ATLAS-TRACE] Unity error stack:\n" + TrimStack(stackTrace, 32));
                }

                DumpSuspiciousFonts();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "[ATLAS-TRACE] Diagnostic collection failed: " + ex.Message);
            }
        }

        private static FontOperationFrame GetActiveFontOperation()
        {
            if (_fontOperationStack == null || _fontOperationStack.Count == 0)
                return null;

            return _fontOperationStack.Peek();
        }

        private void DumpSuspiciousFonts()
        {
            TMP_FontAsset[] fonts;

            try
            {
                fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "[ATLAS-TRACE] Could not enumerate TMP fonts: " + ex.Message);
                return;
            }

            if (fonts == null || fonts.Length == 0)
                return;

            int reported = 0;

            foreach (TMP_FontAsset font in fonts)
            {
                if (font == null)
                    continue;

                bool suspicious =
                    font.atlasPopulationMode == AtlasPopulationMode.Dynamic ||
                    font.atlasWidth >= 4096 ||
                    font.atlasHeight >= 4096;

                if (!suspicious)
                    continue;

                Logger.LogWarning(
                    "[ATLAS-TRACE] Suspicious font: " + DescribeFont(font));

                reported++;
                if (reported >= 25)
                {
                    Logger.LogWarning(
                        "[ATLAS-TRACE] Suspicious-font list truncated at 25 entries.");
                    break;
                }
            }

            if (reported == 0)
            {
                Logger.LogWarning(
                    "[ATLAS-TRACE] No loaded dynamic/4096 TMP_FontAsset was found in the snapshot.");
            }
        }

        private static string DescribeFont(TMP_FontAsset font)
        {
            if (font == null)
                return "(null)";

            try
            {
                Texture2D[] atlases = font.atlasTextures;
                int atlasCount = atlases != null ? atlases.Length : 0;

                string source = font.sourceFontFile != null
                    ? font.sourceFontFile.name
                    : "(null)";

                string material = font.material != null
                    ? font.material.name
                    : "(null)";

                return
                    $"name='{font.name}', " +
                    $"id={font.GetInstanceID()}, " +
                    $"population={font.atlasPopulationMode}, " +
                    $"multiAtlas={font.isMultiAtlasTexturesEnabled}, " +
                    $"atlasSize={font.atlasWidth}x{font.atlasHeight}, " +
                    $"atlasCount={atlasCount}, " +
                    $"sourceFont='{source}', " +
                    $"material='{material}'";
            }
            catch (Exception ex)
            {
                return $"name='{font.name}', descriptionFailed='{ex.Message}'";
            }
        }

        private static string DescribeMethod(MethodBase method)
        {
            if (method == null)
                return "(unknown)";

            string typeName = method.DeclaringType != null
                ? method.DeclaringType.FullName
                : "(unknown-type)";

            return typeName + "." + method.Name;
        }

        private static string DescribeArguments(object[] args)
        {
            if (args == null || args.Length == 0)
                return "(none)";

            var sb = new StringBuilder();
            int shown = Math.Min(args.Length, 8);

            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                    sb.Append(", ");

                object value = args[i];
                sb.Append("arg");
                sb.Append(i);
                sb.Append('=');

                if (value == null)
                {
                    sb.Append("null");
                }
                else if (value is string s)
                {
                    string clipped = s.Length > 80 ? s.Substring(0, 80) + "..." : s;
                    sb.Append('"');
                    sb.Append(clipped.Replace("\r", "\\r").Replace("\n", "\\n"));
                    sb.Append('"');
                    sb.Append(" [");
                    sb.Append(ToCodePoints(clipped));
                    sb.Append(']');
                }
                else if (value is uint u)
                {
                    sb.Append(u);
                    sb.Append(" (U+");
                    sb.Append(u.ToString("X4"));
                    sb.Append(')');
                }
                else if (value is char c)
                {
                    sb.Append('\'');
                    sb.Append(c);
                    sb.Append("' (U+");
                    sb.Append(((int)c).ToString("X4"));
                    sb.Append(')');
                }
                else
                {
                    string text = value.ToString();
                    if (text != null && text.Length > 120)
                        text = text.Substring(0, 120) + "...";
                    sb.Append(text ?? "(null-string)");
                }
            }

            if (args.Length > shown)
                sb.Append(", ...");

            return sb.ToString();
        }

        private static string TrimStack(string stack, int maxLines)
        {
            if (String.IsNullOrWhiteSpace(stack))
                return "(none)";

            string[] lines = stack.Replace("\r\n", "\n").Split('\n');
            int count = Math.Min(lines.Length, maxLines);
            var sb = new StringBuilder();

            for (int i = 0; i < count; i++)
            {
                if (!String.IsNullOrWhiteSpace(lines[i]))
                    sb.AppendLine(lines[i]);
            }

            if (lines.Length > maxLines)
                sb.AppendLine("... stack truncated ...");

            return sb.ToString();
        }

        public static void TmpTextStringPrefix(TMP_Text __instance, ref string __0)
        {
            Plugin p = Instance;
            if (p == null || __instance == null || String.IsNullOrEmpty(__0))
                return;

            if (!ContainsHangulOrJamo(__0))
                return;

            try
            {
                string value = __0;

                if (p._normalizeNfc.Value)
                {
                    string normalized = value.Normalize(NormalizationForm.FormC);

                    if (p._verbose.Value &&
                        !String.Equals(value, normalized, StringComparison.Ordinal))
                    {
                        p.Logger.LogInfo(
                            "NFC: " + ToCodePoints(value) +
                            " -> " + ToCodePoints(normalized));
                    }

                    value = normalized;
                    __0 = normalized;
                }

                p.RouteTextToKoreanFont(__instance, value);
            }
            catch (Exception ex)
            {
                p.Logger.LogWarning("TMP Korean text routing failed: " + ex.Message);
            }
        }

        public static void InputFieldUpdatePrefix(TMP_InputField __instance)
        {
            Plugin p = Instance;
            if (p == null || __instance == null || p._koreanFont == null)
                return;

            try
            {
                string committed = __instance.text ?? String.Empty;
                string composition = Input.compositionString ?? String.Empty;
                string display = committed + composition;

                if (!ContainsHangulOrJamo(display))
                    return;

                string normalized = p._normalizeNfc.Value
                    ? display.Normalize(NormalizationForm.FormC)
                    : display;

                TMP_Text textComponent = __instance.textComponent;
                if (textComponent != null)
                    p.RouteTextToKoreanFont(textComponent, normalized);
            }
            catch (Exception ex)
            {
                if (p._verbose.Value)
                    p.Logger.LogWarning("TMP_InputField fallback routing failed: " + ex.Message);
            }
        }

        private void RouteTextToKoreanFont(TMP_Text text, string value)
        {
            if (_koreanFont == null || text == null || String.IsNullOrEmpty(value))
                return;

            if (!ContainsHangulOrJamo(value))
                return;

            // v4.2+: fallback-only. Do not replace TMP_Text.font.
            AttachFallback(text.font);
        }

        private void AttachFallback(TMP_FontAsset baseFont)
        {
            if (_koreanFont == null || baseFont == null || baseFont == _koreanFont)
                return;

            int id = baseFont.GetInstanceID();
            if (_patchedFontIds.Contains(id))
                return;

            try
            {
                List<TMP_FontAsset> table = baseFont.fallbackFontAssetTable;

                if (table == null)
                {
                    table = new List<TMP_FontAsset>();
                    baseFont.fallbackFontAssetTable = table;
                }

                if (!table.Contains(_koreanFont))
                    table.Insert(0, _koreanFont);

                _patchedFontIds.Add(id);

                if (_verbose.Value)
                    Logger.LogInfo("Attached Korean fallback to: " + baseFont.name);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    $"Could not attach Korean fallback to '{baseFont.name}': {ex.Message}");
            }
        }

        private static bool ContainsHangulOrJamo(string s)
        {
            foreach (char c in s)
            {
                if (IsHangulOrJamo(c))
                    return true;
            }

            return false;
        }

        private static bool IsHangulOrJamo(char c)
        {
            int u = c;

            if (u >= 0x1100 && u <= 0x11FF) return true;
            if (u >= 0x3130 && u <= 0x318F) return true;
            if (u >= 0xA960 && u <= 0xA97F) return true;
            if (u >= 0xAC00 && u <= 0xD7A3) return true;
            if (u >= 0xD7B0 && u <= 0xD7FF) return true;

            return false;
        }

        private static string ToCodePoints(string s)
        {
            if (String.IsNullOrEmpty(s))
                return "(none)";

            var sb = new StringBuilder();

            for (int i = 0; i < s.Length; i++)
            {
                if (i != 0)
                    sb.Append(' ');

                sb.Append("U+");
                sb.Append(((int)s[i]).ToString("X4"));
            }

            return sb.ToString();
        }
    }
}
