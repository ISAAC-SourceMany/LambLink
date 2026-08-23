using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class KoreanFontBundleBuilder
{
    private const string InputFontPath =
        "Assets/Input/KoreanSourceFont.ttf";

    private const string FontAssetPath =
        "Assets/KoreanRuntimeFont.asset";

    private const string BundleName =
        "koreanfont.bundle";

    public static void BuildFromCommandLine()
    {
        try
        {
            string sourceFont = GetArg("-font");

            if (String.IsNullOrWhiteSpace(sourceFont) ||
                !File.Exists(sourceFont))
            {
                throw new FileNotFoundException(
                    "Source font not found.",
                    sourceFont);
            }

            EnsureTmpShaderAvailable();

            Directory.CreateDirectory(
                Path.GetDirectoryName(InputFontPath));

            File.Copy(
                sourceFont,
                InputFontPath,
                true);

            AssetDatabase.ImportAsset(
                InputFontPath,
                ImportAssetOptions.ForceSynchronousImport);

            AssetDatabase.Refresh();

            Font source =
                AssetDatabase.LoadAssetAtPath<Font>(
                    InputFontPath);

            if (source == null)
            {
                throw new InvalidOperationException(
                    "Unity could not import source font: " +
                    InputFontPath);
            }

            Debug.Log(
                "KOREAN_FONT_SOURCE=" +
                sourceFont);

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                FontAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(
                    FontAssetPath);
            }

            TMP_FontAsset fontAsset =
                TMP_FontAsset.CreateFontAsset(
                    source,
                    64,
                    8,
                    GlyphRenderMode.SDFAA,
                    2048,
                    2048,
                    AtlasPopulationMode.Dynamic,
                    true);

            if (fontAsset == null)
            {
                throw new InvalidOperationException(
                    "TMP_FontAsset.CreateFontAsset returned null.");
            }

            fontAsset.name =
                "KoreanRuntimeFont";

            fontAsset.isMultiAtlasTexturesEnabled =
                true;

            string characters =
                BuildKoreanCharacterSet();

            Debug.Log(
                "KOREAN_FONT_PREBAKE_START=" +
                characters.Length +
                " characters");

            string missing;

            bool allAdded =
                fontAsset.TryAddCharacters(
                    characters,
                    out missing,
                    false);

            Texture2D[] atlases =
                fontAsset.atlasTextures;

            int atlasCount =
                atlases != null
                    ? atlases.Length
                    : 0;

            Debug.Log(
                "KOREAN_FONT_PREBAKE_RESULT=" +
                "allAdded=" + allAdded +
                ", requested=" + characters.Length +
                ", missing=" +
                (missing != null ? missing.Length : 0) +
                ", atlases=" + atlasCount);

            if (!String.IsNullOrEmpty(missing))
            {
                Debug.LogWarning(
                    "KOREAN_FONT_PREBAKE_MISSING_COUNT=" +
                    missing.Length);

                Debug.LogWarning(
                    "Missing codepoints: " +
                    ToCodePoints(missing));
            }

            // Important: runtime must never populate the atlas.
            fontAsset.atlasPopulationMode =
                AtlasPopulationMode.Static;

            AssetDatabase.CreateAsset(
                fontAsset,
                FontAssetPath);

            if (fontAsset.material != null &&
                !AssetDatabase.Contains(fontAsset.material))
            {
                AssetDatabase.AddObjectToAsset(
                    fontAsset.material,
                    fontAsset);
            }

            atlases =
                fontAsset.atlasTextures;

            if (atlases != null)
            {
                foreach (Texture2D atlas in atlases)
                {
                    if (atlas != null &&
                        !AssetDatabase.Contains(atlas))
                    {
                        AssetDatabase.AddObjectToAsset(
                            atlas,
                            fontAsset);
                    }
                }
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();

            AssetDatabase.ImportAsset(
                FontAssetPath,
                ImportAssetOptions.ForceSynchronousImport);

            AssetDatabase.Refresh();

            AssetImporter importer =
                AssetImporter.GetAtPath(
                    FontAssetPath);

            if (importer == null)
            {
                throw new InvalidOperationException(
                    "Could not get importer for " +
                    FontAssetPath);
            }

            importer.assetBundleName =
                BundleName;

            importer.SaveAndReimport();

            string buildDir =
                Path.GetFullPath("Build");

            if (Directory.Exists(buildDir))
                Directory.Delete(buildDir, true);

            Directory.CreateDirectory(buildDir);

            AssetBundleManifest manifest =
                BuildPipeline.BuildAssetBundles(
                    buildDir,
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.StandaloneWindows64);

            if (manifest == null)
            {
                throw new InvalidOperationException(
                    "BuildPipeline.BuildAssetBundles returned null.");
            }

            string built =
                Path.Combine(
                    buildDir,
                    BundleName);

            if (!File.Exists(built))
            {
                throw new FileNotFoundException(
                    "AssetBundle build completed but bundle is missing.",
                    built);
            }

            Debug.Log(
                "KOREAN_FONT_BUNDLE_OK=" +
                built);
        }
        catch (Exception ex)
        {
            Debug.LogError(
                "KOREAN_FONT_BUNDLE_FAILED");

            Debug.LogException(ex);
            throw;
        }
    }

    private static void EnsureTmpShaderAvailable()
    {
        Shader shader =
            Shader.Find(
                "TextMeshPro/Mobile/Distance Field");

        if (shader != null)
            return;

        throw new InvalidOperationException(
            "TextMeshPro SDF shader is unavailable. " +
            "Open the FontBundleBuilder project once in Unity and use " +
            "Window > TextMeshPro > Import TMP Essential Resources, " +
            "then rerun the build.");
    }

    private static string BuildKoreanCharacterSet()
    {
        var sb =
            new StringBuilder(12000);

        for (int u = 0x0020; u <= 0x007E; u++)
            sb.Append((char)u);

        for (int u = 0x1100; u <= 0x11FF; u++)
            sb.Append((char)u);

        for (int u = 0x3130; u <= 0x318F; u++)
            sb.Append((char)u);

        for (int u = 0xA960; u <= 0xA97F; u++)
            sb.Append((char)u);

        for (int u = 0xAC00; u <= 0xD7A3; u++)
            sb.Append((char)u);

        for (int u = 0xD7B0; u <= 0xD7FF; u++)
            sb.Append((char)u);

        return sb.ToString();
    }

    private static string ToCodePoints(
        string value)
    {
        if (String.IsNullOrEmpty(value))
            return "(none)";

        var sb =
            new StringBuilder();

        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0)
                sb.Append(' ');

            sb.Append("U+");
            sb.Append(
                ((int)value[i]).ToString("X4"));
        }

        return sb.ToString();
    }

    private static string GetArg(
        string name)
    {
        string[] args =
            Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (String.Equals(
                args[i],
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
