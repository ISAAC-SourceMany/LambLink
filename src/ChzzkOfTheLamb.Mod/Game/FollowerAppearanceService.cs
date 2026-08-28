using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using ChzzkOfTheLamb.Protocol;
using HarmonyLib;
using UnityEngine;

namespace ChzzkOfTheLamb.Mod.Game;

/// <summary>
/// Runtime adapter for the game's follower-form catalog.
/// Uses the live WorshipperData + save unlock lists so DLC/game updates do not require a
/// hard-coded CHZZK form table.
/// </summary>
public sealed class FollowerAppearanceService(ManualLogSource log, GameSaveService saves, Action<string>? diagnosticStage = null)
{
    private string? _lastDiscoverySummary;
    private int _catalogBuildSequence;

    public FollowerAppearanceCatalog BuildCatalog(bool includeModded, bool includeSpecial)
    {
        var requestId = Interlocked.Increment(ref _catalogBuildSequence);
        var started = Stopwatch.GetTimestamp();
        void Stage(string name, string details = "")
        {
            diagnosticStage?.Invoke($"CATALOG/{requestId}/{name}");
            log.LogInfo($"[APPEARANCE][BUILD][{name}] id={requestId}{(string.IsNullOrEmpty(details) ? string.Empty : ", " + details)}");
        }

        Stage("BEGIN", $"includeModded={includeModded}, includeSpecial={includeSpecial}");
        var catalog = new FollowerAppearanceCatalog { SaveId = "unknown" };

        try
        {
            Stage("SAVE-PROBE-BEGIN");
            var inGame = PlayerFarming.Instance != null;
            if (inGame) catalog.SaveId = saves.GetCurrentSaveId();
            Stage("SAVE-PROBE-END", $"inGame={inGame}, save={catalog.SaveId}");

            Stage("TYPE-LOOKUP-BEGIN", "type=WorshipperData");
            var worshipperType = FindTypeBySimpleName("WorshipperData");
            Stage("TYPE-LOOKUP-END", $"found={worshipperType != null}");
            if (worshipperType is null)
            {
                LogDiscoveryOnce("WorshipperData type not found in loaded assemblies.");
                Stage("END", $"forms=0, elapsedMs={ElapsedMilliseconds(started):F1}, reason=type-not-found");
                return catalog;
            }

            Stage("SINGLETON-BEGIN", $"type={worshipperType.FullName}");
            var instance = ReadStaticFirst(worshipperType, "_Instance", "Instance", "instance");
            Stage("SINGLETON-END", $"initialized={instance != null}");
            if (instance is null)
            {
                LogDiscoveryOnce($"{worshipperType.FullName} found, but its singleton is not initialized yet.");
                Stage("END", $"forms=0, elapsedMs={ElapsedMilliseconds(started):F1}, reason=singleton-null");
                return catalog;
            }

            Stage("CHARACTERS-BEGIN");
            var raw = ReadFirst(instance, worshipperType, "Characters", "characters", "Skins", "FollowerSkins") as IList;
            Stage("CHARACTERS-END", $"count={(raw == null ? -1 : raw.Count)}");
            if (raw is null)
            {
                LogDiscoveryOnce($"{worshipperType.FullName} singleton found, but Characters is not indexable.");
                Stage("END", $"forms=0, elapsedMs={ElapsedMilliseconds(started):F1}, reason=characters-null");
                return catalog;
            }

            // FollowerSkinsUnlocked contains the actual skin codenames the save has unlocked.
            // FollowerSkinsBlacklist is NOT a manual-selection deny list; it is used by the game
            // to stop certain skins appearing through normal/random spawning. Using it here was
            // the reason devbridge3 produced selectable=0 even with 28 unlocked forms.
            Stage("UNLOCKS-BEGIN");
            var unlocked = ReadStringSetFromDataManager("FollowerSkinsUnlocked", "FollowerFormsUnlocked", "SkinsUnlocked");
            Stage("UNLOCKS-END", $"count={unlocked.Count}");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mapped = 0;
            var missing = 0;
            Stage("PALETTE-BEGIN");
            var globalPalette = DiscoverGlobalPalette(instance, worshipperType);
            Stage("PALETTE-END", $"count={globalPalette.Count}");

            if (unlocked.Count > 0)
            {
                Stage("INDEX-METHOD-BEGIN");
                var getIndex = worshipperType.GetMethod("GetSkinIndexFromName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                Stage("INDEX-METHOD-END", $"found={getIndex != null}");
                var orderedUnlocked = unlocked.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                for (var formIndex = 0; formIndex < orderedUnlocked.Count; formIndex++)
                {
                    var formId = orderedUnlocked[formIndex];
                    Stage("FORM-BEGIN", $"index={formIndex + 1}/{orderedUnlocked.Count}, form={formId}");
                    object? item = null;
                    try
                    {
                        if (getIndex != null)
                        {
                            diagnosticStage?.Invoke($"CATALOG/{requestId}/FORM/{formId}/INDEX-INVOKE");
                            var idxObj = getIndex.Invoke(instance, new object[] { formId });
                            if (idxObj is int idx && idx >= 0 && idx < raw.Count)
                                item = raw[idx];
                        }
                    }
                    catch { }

                    // Fallback: scan raw data and match its skin/name field to the unlocked codename.
                    if (item is null)
                    {
                        diagnosticStage?.Invoke($"CATALOG/{requestId}/FORM/{formId}/RAW-SCAN");
                        foreach (var candidate in raw)
                        {
                            if (candidate is null) continue;
                            var t = candidate.GetType();
                            var candidateId = ReadFirst(candidate, t, "SkinName", "skinName", "Name", "FormName", "ID", "Id", "id")?.ToString();
                            if (string.Equals(candidateId, formId, StringComparison.OrdinalIgnoreCase))
                            {
                                item = candidate;
                                break;
                            }
                        }
                    }

                    if (item is null)
                    {
                        missing++;
                        Stage("FORM-END", $"index={formIndex + 1}/{orderedUnlocked.Count}, form={formId}, result=missing");
                        continue;
                    }

                    diagnosticStage?.Invoke($"CATALOG/{requestId}/FORM/{formId}/DESCRIBE");
                    var form = DescribeKnownId(item, formId, globalPalette);
                    if (!seen.Add(form.FormId))
                    {
                        Stage("FORM-END", $"index={formIndex + 1}/{orderedUnlocked.Count}, form={formId}, result=duplicate");
                        continue;
                    }
                    if (!includeSpecial && form.IsSpecial)
                    {
                        Stage("FORM-END", $"index={formIndex + 1}/{orderedUnlocked.Count}, form={formId}, result=special-filtered");
                        continue;
                    }
                    if (!includeModded && form.IsModded)
                    {
                        Stage("FORM-END", $"index={formIndex + 1}/{orderedUnlocked.Count}, form={formId}, result=modded-filtered");
                        continue;
                    }
                    catalog.Forms.Add(form);
                    mapped++;
                    Stage("FORM-END", $"index={formIndex + 1}/{orderedUnlocked.Count}, form={formId}, variants={form.VariantIds.Count}, colors={form.ColorIds.Count}");
                }
            }
            else
            {
                // Fallback for saves/builds where no unlock list is exposed. Do not use the
                // blacklist as a selection filter; instead expose non-special raw forms and let
                // final validation occur against the live game before applying one.
                for (var rawIndex = 0; rawIndex < raw.Count; rawIndex++)
                {
                    diagnosticStage?.Invoke($"CATALOG/{requestId}/RAW/{rawIndex + 1}-OF-{raw.Count}");
                    var candidate = raw[rawIndex];
                    if (candidate is null) continue;
                    var t = candidate.GetType();
                    var id = ReadFirst(candidate, t, "SkinName", "skinName", "Name", "FormName", "ID", "Id", "id")?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
                    var form = DescribeKnownId(candidate, id, globalPalette);
                    if (!includeSpecial && form.IsSpecial) continue;
                    if (!includeModded && form.IsModded) continue;
                    catalog.Forms.Add(form);
                }
                mapped = catalog.Forms.Count;
            }

            Stage("SORT-BEGIN", $"forms={catalog.Forms.Count}");
            catalog.Forms.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            Stage("SORT-END", $"forms={catalog.Forms.Count}");
            var variantForms = catalog.Forms.Count(x => x.VariantIds.Count > 0);
            var colorForms = catalog.Forms.Count(x => x.ColorIds.Count > 0);
            var variantOptions = catalog.Forms.Sum(x => x.VariantIds.Count);
            var colorOptions = catalog.Forms.Sum(x => x.ColorIds.Count);
            var paletteColors = catalog.Forms.Sum(x => x.ColorHexById.Count);
            LogDiscoveryOnce($"Follower forms: source={worshipperType.FullName}, raw={raw.Count}, unlockedList={unlocked.Count}, mapped={mapped}, missing={missing}, selectable={catalog.Forms.Count}, variantForms={variantForms}, variantOptions={variantOptions}, colorForms={colorForms}, colorOptions={colorOptions}, paletteColors={paletteColors}.");
            Stage("END", $"save={catalog.SaveId}, forms={catalog.Forms.Count}, elapsedMs={ElapsedMilliseconds(started):F1}");
        }
        catch (Exception ex)
        {
            Stage("FAILED", $"elapsedMs={ElapsedMilliseconds(started):F1}, error={ex.GetBaseException().Message}");
            log.LogWarning($"Appearance catalog discovery failed: {ex}");
        }
        return catalog;
    }

    private static double ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

    public bool Validate(FollowerAppearanceSelection? selection, FollowerAppearanceCatalog catalog)
    {
        if (selection is null) return true; // null = game's normal random appearance.
        var form = catalog.Forms.FirstOrDefault(x => string.Equals(x.FormId, selection.FormId, StringComparison.OrdinalIgnoreCase) && x.IsUnlocked);
        if (form is null) return false;
        if (selection.VariantId is not null && form.VariantIds.Count > 0 && !form.VariantIds.Contains(selection.VariantId)) return false;
        if (selection.ColorId is not null && form.ColorIds.Count > 0 && !form.ColorIds.Contains(selection.ColorId)) return false;
        return true;
    }

    public bool TryApply(object target, FollowerAppearanceSelection? selection)
    {
        if (selection is null) return true;
        var info = FindFollowerInfo(target, 3);
        if (info is null) return false;

        try
        {
            var worshipperType = AccessTools.TypeByName("WorshipperData");
            var instance = worshipperType == null ? null : ReadStaticFirst(worshipperType, "_Instance", "Instance", "instance");
            if (instance is null || worshipperType is null) return false;
            var getIndex = worshipperType.GetMethod(
                "GetSkinIndexFromName",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);
            var idxObj = getIndex?.Invoke(instance, new object[] { selection.FormId });
            if (idxObj is not int idx || idx < 0) return false;
            var characters = ReadFirst(instance, worshipperType, "Characters") as IList;
            if (characters is null || idx >= characters.Count || characters[idx] is null) return false;
            var character = characters[idx]!;
            var skins = ReadFirst(character, character.GetType(), "Skin") as IList;
            var colours = ReadFirst(character, character.GetType(), "SlotAndColours") as IList;
            var variant = ParseIndex(selection.VariantId, 0);
            var colour = ParseIndex(selection.ColorId, 0);
            if (skins is null || variant < 0 || variant >= skins.Count) return false;
            if (selection.ColorId is not null && (colours is null || colour < 0 || colour >= colours.Count)) return false;

            var skinEntry = skins[variant];
            var actualSkinName = skinEntry is null ? null : ReadFirst(skinEntry, skinEntry.GetType(), "Skin")?.ToString();
            if (string.IsNullOrWhiteSpace(actualSkinName)) return false;

            var ok = TryWriteConvertibleValue(info, new[] { "SkinCharacter" }, idx.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ok &= TryWriteConvertibleValue(info, new[] { "SkinVariation", "SkinVariant", "Variant", "VariantId" }, variant.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (selection.ColorId is not null)
                ok &= TryWriteConvertibleValue(info, new[] { "SkinColour", "SkinColor", "Colour", "Color" }, colour.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ok &= TryWriteConvertibleValue(info, new[] { "SkinName", "Form", "FormId" }, actualSkinName!);
            return ok;
        }
        catch (Exception ex)
        {
            log.LogWarning($"Could not synchronize SkinCharacter for form {selection.FormId}: {ex.Message}");
        }

        return false;
    }

    public FollowerAppearanceSelection? ReadAppliedSelection(object target, string? requestedFormId)
    {
        var info = FindFollowerInfo(target, 3);
        if (info is null) return null;
        var formId = string.IsNullOrWhiteSpace(requestedFormId)
            ? ReadFirst(info, info.GetType(), "SkinName")?.ToString()
            : requestedFormId;
        if (string.IsNullOrWhiteSpace(formId)) return null;
        return new FollowerAppearanceSelection
        {
            FormId = formId!,
            VariantId = ReadFirst(info, info.GetType(), "SkinVariation", "SkinVariant")?.ToString(),
            ColorId = ReadFirst(info, info.GetType(), "SkinColour", "SkinColor")?.ToString()
        };
    }

    private static int ParseIndex(string? value, int fallback)
        => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private FollowerFormDescriptor DescribeKnownId(object item, string formId, IReadOnlyList<Color> globalPalette)
    {
        var type = item.GetType();
        var display = ReadFirst(item, type, "LocalizedName", "DisplayName", "displayName", "Name", "SkinName")?.ToString() ?? formId;
        if (string.IsNullOrWhiteSpace(display)) display = formId;

        // Confirmed from COTL 1.5.25.1049 Assembly-CSharp.dll:
        //   UIAppearanceMenuController_Variant iterates SkinAndData.Skin and persists
        //   FollowerBrainInfo.SkinVariation as the selected list index.
        //   UIAppearanceMenuController_Colour iterates SkinAndData.SlotAndColours and persists
        //   FollowerBrainInfo.SkinColour as the selected list index.
        // Do not guess member names here; use the exact game data structures.
        var skinList = ReadFirst(item, type, "Skin") as IList;
        var colourList = ReadFirst(item, type, "SlotAndColours") as IList;
        var variantIds = skinList is null ? new List<string>() : RangeIds(skinList.Count);
        var colorIds = colourList is null ? new List<string>() : RangeIds(colourList.Count);

        // Preview image generation is intentionally disabled. The viewer UI keeps the
        // form / colour / variant selectors, but no longer attempts to render or extract
        // game assets for example images.
        var variantPreviewMap = new Dictionary<string, string>();

        // Each colour entry is a SlotsAndColours object. Its AllColor field is the base tint
        // used by the game's colour picker, so export that exact value instead of heuristically
        // walking arbitrary Color fields.
        var colorMap = new Dictionary<string, string>();
        if (colourList is not null)
        {
            for (var i = 0; i < colourList.Count; i++)
            {
                var entry = colourList[i];
                if (entry is null) continue;
                var allColor = ReadFirst(entry, entry.GetType(), "AllColor");
                if (allColor is Color c) colorMap[i.ToString()] = ColorToHex(c);
                else if (allColor is Color32 c32) colorMap[i.ToString()] = ColorToHex(c32);
            }
        }
        if (colorMap.Count == 0 && globalPalette.Count > 0)
        {
            for (var i = 0; i < colorIds.Count && i < globalPalette.Count; i++)
                colorMap[colorIds[i]] = ColorToHex(globalPalette[i]);
        }

        return new FollowerFormDescriptor
        {
            FormId = formId,
            DisplayName = display,
            IsUnlocked = true,
            IsSpecial = ReadBool(item, type, "Special", "IsSpecial", "Unique", "IsUnique") || LooksSpecial(formId),
            IsModded = ReadBool(item, type, "Modded", "IsModded") || LooksModded(item, type),
            VariantIds = variantIds,
            ColorIds = colorIds,
            FormPreviewPngBase64 = null,
            VariantPreviewPngBase64 = variantPreviewMap,
            ColorHexById = colorMap
        };
    }

    private static FollowerFormDescriptor Describe(object item, HashSet<string> unlocked, HashSet<string> blacklist, bool hasUnlockData)
    {
        var type = item.GetType();
        var formId = ReadFirst(item, type, "SkinName", "skinName", "Name", "FormName", "ID", "Id", "id")?.ToString() ?? string.Empty;
        var display = ReadFirst(item, type, "LocalizedName", "DisplayName", "displayName", "Name", "SkinName")?.ToString() ?? formId;
        if (string.IsNullOrWhiteSpace(display)) display = formId;

        var explicitlyLocked = ReadBool(item, type, "Locked", "IsLocked", "locked", "isLocked");
        var explicitlyUnlocked = ReadNullableBool(item, type, "Unlocked", "IsUnlocked", "unlocked", "isUnlocked");
        var blacklisted = blacklist.Contains(formId);
        var isUnlocked = explicitlyUnlocked ?? (!explicitlyLocked && (!hasUnlockData || unlocked.Contains(formId)));
        if (blacklisted) isUnlocked = false;

        var special = ReadBool(item, type, "Special", "IsSpecial", "Unique", "IsUnique") || LooksSpecial(formId);
        var modded = ReadBool(item, type, "Modded", "IsModded") || LooksModded(item, type);

        return new FollowerFormDescriptor
        {
            FormId = formId,
            DisplayName = display,
            IsUnlocked = isUnlocked,
            IsSpecial = special,
            IsModded = modded,
            VariantIds = DiscoverOptionIds(item, type,
                new[] { "VariantIds", "Variants", "SkinVariations", "Variations", "SkinVariants", "VariationSprites", "VariantSprites" },
                new[] { "variant", "variation" }),
            ColorIds = DiscoverOptionIds(item, type,
                new[] { "ColorIds", "ColourIds", "Colors", "Colours", "SkinColors", "SkinColours", "ColorVariations", "ColourVariations", "Palettes", "Palette" },
                new[] { "color", "colour", "palette" })
        };
    }

    internal static object? FindFollowerInfo(object? root, int depth)
    {
        if (root is null || depth < 0) return null;
        var type = root.GetType();
        if (type.Name == "FollowerInfo") return root;
        foreach (var name in new[] { "Info", "_info", "Brain", "_directInfoAccess", "FollowerInfo", "Data" })
        {
            var value = ReadFirst(root, type, name);
            var found = FindFollowerInfo(value, depth - 1);
            if (found is not null) return found;
        }
        return null;
    }

    internal static bool TryWrite(object target, IEnumerable<string> names, object value)
    {
        var type = target.GetType();
        foreach (var name in names)
        {
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p?.CanWrite == true && p.PropertyType.IsInstanceOfType(value)) { p.SetValue(target, value); return true; }
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f is not null && f.FieldType.IsInstanceOfType(value)) { f.SetValue(target, value); return true; }
        }
        return false;
    }

    private static bool TryWriteConvertibleValue(object target, IEnumerable<string> names, string value)
    {
        var type = target.GetType();
        foreach (var name in names)
        {
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p?.CanWrite == true && TryConvert(value, p.PropertyType, out var converted)) { p.SetValue(target, converted); return true; }
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f is not null && TryConvert(value, f.FieldType, out converted)) { f.SetValue(target, converted); return true; }
        }
        return false;
    }

    private static bool TryConvert(string value, Type target, out object? converted)
    {
        try
        {
            var nullable = Nullable.GetUnderlyingType(target);
            if (nullable is not null) target = nullable;
            if (target == typeof(string)) { converted = value; return true; }
            if (target.IsEnum) { converted = Enum.Parse(target, value, true); return true; }
            converted = Convert.ChangeType(value, target); return true;
        }
        catch { converted = null; return false; }
    }

    private HashSet<string> ReadStringSetFromDataManager(params string[] names)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var type = FindTypeBySimpleName("DataManager");
            if (type is null) return result;
            var instance = ReadStaticFirst(type, "Instance", "_Instance", "instance");
            if (instance is null) return result;
            foreach (var name in names)
            {
                var value = ReadFirst(instance, type, name);
                AddStrings(value, result);
                if (result.Count > 0) break;
            }
        }
        catch { }
        return result;
    }

    private static void AddStrings(object? value, HashSet<string> result)
    {
        if (value is null) return;
        if (value is string s)
        {
            if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
            return;
        }
        if (value is not IEnumerable enumerable) return;
        foreach (var item in enumerable)
        {
            var text = item?.ToString();
            if (!string.IsNullOrWhiteSpace(text)) result.Add(text!);
        }
    }

    private static List<string> ReadStringList(object obj, Type type, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadFirst(obj, type, name);
            if (value is null || value is string || value is not IEnumerable enumerable) continue;
            var list = new List<string>();
            foreach (var item in enumerable)
            {
                if (item is null) continue;
                var id = ReadFirst(item, item.GetType(), "Id", "ID", "Name", "name")?.ToString() ?? item.ToString();
                if (!string.IsNullOrWhiteSpace(id) && !list.Contains(id!)) list.Add(id!);
            }
            if (list.Count > 0) return list;
        }
        return new List<string>();
    }



    private static List<Sprite> DiscoverSprites(object obj, Type type, string[] memberTokens)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var result = new List<Sprite>();
        void Add(object? value)
        {
            if (value is Sprite sprite)
            {
                if (!result.Contains(sprite)) result.Add(sprite);
                return;
            }
            if (value is string || value is not IEnumerable enumerable) return;
            foreach (var item in enumerable)
                if (item is Sprite s && !result.Contains(s)) result.Add(s);
        }

        foreach (var p in type.GetProperties(flags))
        {
            if (p.GetMethod is null || !NameMatches(p.Name, memberTokens)) continue;
            try { Add(p.GetValue(obj)); } catch { }
        }
        foreach (var f in type.GetFields(flags))
        {
            if (!NameMatches(f.Name, memberTokens)) continue;
            try { Add(f.GetValue(obj)); } catch { }
        }
        return result;
    }

    private static string? TryExportBestSpritePng(object obj, Type type, string formId)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var candidates = new List<(int Score, Sprite Sprite)>();
        void Add(string memberName, object? value)
        {
            if (value is Sprite sprite)
            {
                var score = ScoreSprite(memberName, sprite.name, formId);
                candidates.Add((score, sprite));
                return;
            }
            if (value is string || value is not IEnumerable enumerable) return;
            foreach (var item in enumerable)
            {
                if (item is not Sprite s) continue;
                candidates.Add((ScoreSprite(memberName, s.name, formId), s));
            }
        }
        foreach (var p in type.GetProperties(flags))
        {
            if (p.GetMethod is null) continue;
            try { Add(p.Name, p.GetValue(obj)); } catch { }
        }
        foreach (var f in type.GetFields(flags))
        {
            try { Add(f.Name, f.GetValue(obj)); } catch { }
        }

        foreach (var candidate in candidates.OrderByDescending(x => x.Score))
        {
            var png = TryExportSpritePng(candidate.Sprite);
            if (!string.IsNullOrWhiteSpace(png)) return png;
        }
        return null;
    }

    private static int ScoreSprite(string memberName, string spriteName, string formId)
    {
        var m = (memberName ?? string.Empty).ToLowerInvariant();
        var n = (spriteName ?? string.Empty).ToLowerInvariant();
        var f = (formId ?? string.Empty).ToLowerInvariant();
        var score = 0;
        if (m.Contains("icon")) score += 50;
        if (m.Contains("portrait")) score += 45;
        if (m.Contains("head")) score += 35;
        if (m.Contains("form") || m.Contains("skin")) score += 25;
        if (!string.IsNullOrWhiteSpace(f) && n.Contains(f)) score += 40;
        if (n.Contains("icon")) score += 20;
        if (n.Contains("head")) score += 15;
        return score;
    }

    private static string? TryExportSpritePng(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null) return null;
        RenderTexture? rt = null;
        Texture2D? copy = null;
        var previous = RenderTexture.active;
        try
        {
            var texture = sprite.texture;
            rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;

            var rect = sprite.textureRect;
            var width = Math.Max(1, (int)Math.Round(rect.width));
            var height = Math.Max(1, (int)Math.Round(rect.height));
            copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0, false);
            copy.Apply(false, false);
            var bytes = copy.EncodeToPNG();
            return bytes == null || bytes.Length == 0 ? null : Convert.ToBase64String(bytes);
        }
        catch { return null; }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (copy != null) UnityEngine.Object.Destroy(copy);
        }
    }

    private static string? TryExportLoadedSpritePng(string formId, IReadOnlyList<Sprite> loadedSprites)
    {
        if (loadedSprites is null || loadedSprites.Count == 0 || string.IsNullOrWhiteSpace(formId)) return null;
        var compact = new string(formId.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var scored = new List<(int Score, Sprite Sprite)>();
        foreach (var sprite in loadedSprites)
        {
            if (sprite == null || sprite.texture == null) continue;
            var name = sprite.name ?? string.Empty;
            var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            var score = 0;
            if (normalized == compact) score += 200;
            if (normalized.Contains(compact)) score += 100;
            if (normalized.Contains("follower")) score += 35;
            if (normalized.Contains("skin")) score += 30;
            if (normalized.Contains("form")) score += 25;
            if (normalized.Contains("icon")) score += 40;
            if (normalized.Contains("head")) score += 30;
            if (normalized.Contains("portrait")) score += 35;
            if (score < 100) continue;
            var r = sprite.rect;
            if (r.width < 16 || r.height < 16 || r.width > 1024 || r.height > 1024) continue;
            scored.Add((score, sprite));
        }
        foreach (var candidate in scored.OrderByDescending(x => x.Score).Take(12))
        {
            var png = TryExportSpritePng(candidate.Sprite);
            if (!string.IsNullOrWhiteSpace(png)) return png;
        }
        return null;
    }

    private static List<Color> DiscoverPaletteFromSkinData(object item, Type type)
    {
        // COTL 1.5.25 exposes SkinAndData.SlotAndColours rather than a top-level palette.
        // Walk that structure recursively and collect Color/Color32 values while preserving
        // first-seen order. This gives the web UI the actual game palette where available.
        var result = new List<Color>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddColor(Color c)
        {
            var key = ColorToHex(c);
            if (seen.Add(key)) result.Add(c);
        }
        void Walk(object? value, int depth)
        {
            if (value is null || depth < 0 || result.Count >= 128) return;
            if (value is Color c) { AddColor(c); return; }
            if (value is Color32 c32) { AddColor(c32); return; }
            if (value is string) return;
            if (value is IEnumerable enumerable)
            {
                foreach (var child in enumerable) Walk(child, depth - 1);
                return;
            }
            var vt = value.GetType();
            if (vt.IsPrimitive || vt.IsEnum || vt == typeof(decimal)) return;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var p in vt.GetProperties(flags))
            {
                if (p.GetMethod is null || p.GetIndexParameters().Length != 0) continue;
                var n = p.Name.ToLowerInvariant();
                if (!(n.Contains("color") || n.Contains("colour") || n.Contains("palette") || n.Contains("slot"))) continue;
                try { Walk(p.GetValue(value), depth - 1); } catch { }
            }
            foreach (var f in vt.GetFields(flags))
            {
                var n = f.Name.ToLowerInvariant();
                if (!(n.Contains("color") || n.Contains("colour") || n.Contains("palette") || n.Contains("slot"))) continue;
                try { Walk(f.GetValue(value), depth - 1); } catch { }
            }
        }
        var root = ReadFirst(item, type, "SlotAndColours", "SlotAndColors", "slotAndColours", "slotAndColors");
        Walk(root, 4);
        return result;
    }

    private static List<Color> DiscoverGlobalPalette(object instance, Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var candidates = new List<List<Color>>();
        void Add(object? value)
        {
            if (value is string || value is not IEnumerable enumerable) return;
            var colors = new List<Color>();
            foreach (var item in enumerable)
            {
                if (item is Color c) colors.Add(c);
                else if (item is Color32 c32) colors.Add(c32);
                else { colors.Clear(); break; }
            }
            if (colors.Count >= 4) candidates.Add(colors);
        }
        foreach (var p in type.GetProperties(flags))
        {
            if (p.GetMethod is null || !NameMatches(p.Name, new[] { "color", "colour", "palette" })) continue;
            try { Add(p.GetValue(instance)); } catch { }
        }
        foreach (var f in type.GetFields(flags))
        {
            if (!NameMatches(f.Name, new[] { "color", "colour", "palette" })) continue;
            try { Add(f.GetValue(instance)); } catch { }
        }
        return candidates.OrderByDescending(x => x.Count).FirstOrDefault() ?? new List<Color>();
    }

    private static string ColorToHex(Color color)
    {
        var c = (Color32)color;
        return $"#{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
    }

    /// <summary>
    /// Cult of the Lamb has changed the member names used by SkinAndData across builds.
    /// First try known names, then inspect any enumerable/count-like members whose names
    /// contain variant/variation or color/colour/palette.  Numeric indexes are intentional:
    /// FollowerInfo persists SkinVariation and SkinColour as numeric values in the save data.
    /// </summary>
    private static List<string> DiscoverOptionIds(object obj, Type type, string[] explicitNames, string[] memberTokens)
    {
        var direct = ReadStringList(obj, type, explicitNames);
        if (direct.Count > 0) return NormalizeOptionIds(direct);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var p in type.GetProperties(flags))
        {
            if (p.GetMethod is null || !NameMatches(p.Name, memberTokens)) continue;
            try
            {
                var ids = OptionIdsFromValue(p.GetValue(obj));
                if (ids.Count > 0) return ids;
            }
            catch { }
        }
        foreach (var f in type.GetFields(flags))
        {
            if (!NameMatches(f.Name, memberTokens)) continue;
            try
            {
                var ids = OptionIdsFromValue(f.GetValue(obj));
                if (ids.Count > 0) return ids;
            }
            catch { }
        }

        // Some builds expose only a count. A count of N means valid stored indexes 0..N-1.
        foreach (var p in type.GetProperties(flags))
        {
            if (p.GetMethod is null || !NameMatches(p.Name, memberTokens) || p.Name.IndexOf("count", StringComparison.OrdinalIgnoreCase) < 0) continue;
            try { if (TryCount(p.GetValue(obj), out var count)) return RangeIds(count); } catch { }
        }
        foreach (var f in type.GetFields(flags))
        {
            if (!NameMatches(f.Name, memberTokens) || f.Name.IndexOf("count", StringComparison.OrdinalIgnoreCase) < 0) continue;
            try { if (TryCount(f.GetValue(obj), out var count)) return RangeIds(count); } catch { }
        }

        return new List<string>();
    }

    private static bool NameMatches(string name, IEnumerable<string> tokens)
        => tokens.Any(t => name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);

    private static List<string> OptionIdsFromValue(object? value)
    {
        if (value is null || value is string) return new List<string>();
        if (value is IEnumerable enumerable)
        {
            var items = new List<object?>();
            foreach (var item in enumerable) items.Add(item);
            if (items.Count == 0) return new List<string>();

            var named = new List<string>();
            foreach (var item in items)
            {
                if (item is null) continue;
                var t = item.GetType();
                var id = ReadFirst(item, t, "Id", "ID", "Name", "name", "Index", "index")?.ToString();
                if (!string.IsNullOrWhiteSpace(id) && !named.Contains(id!)) named.Add(id!);
            }

            // Unity Color/Sprite/material objects often stringify to noisy values; the game
            // persists the selection as an integer, so indexes are the safest portable IDs.
            if (named.Count != items.Count || named.Any(x => x.Length > 32 || x.Contains("(")))
                return RangeIds(items.Count);
            return NormalizeOptionIds(named);
        }
        return new List<string>();
    }

    private static List<string> NormalizeOptionIds(IEnumerable<string> values)
    {
        var result = new List<string>();
        foreach (var value in values)
        {
            var v = value?.Trim();
            if (string.IsNullOrWhiteSpace(v) || result.Contains(v!)) continue;
            result.Add(v!);
        }
        return result;
    }

    private static bool TryCount(object? value, out int count)
    {
        try
        {
            count = Convert.ToInt32(value);
            return count > 0 && count <= 128;
        }
        catch { count = 0; return false; }
    }

    private static List<string> RangeIds(int count)
    {
        var result = new List<string>();
        if (count <= 0 || count > 128) return result;
        for (var i = 0; i < count; i++) result.Add(i.ToString());
        return result;
    }

    private static bool LooksSpecial(string formId)
    {
        if (string.IsNullOrWhiteSpace(formId)) return false;
        var specialTokens = new[] { "Boss", "CultLeader", "Aym", "Baal", "Sozo", "Bishop", "Death Cat" };
        return specialTokens.Any(x => formId.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool LooksModded(object item, Type type)
    {
        var source = type.Assembly.GetName().Name ?? string.Empty;
        if (!source.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)) return true;
        var guid = ReadFirst(item, type, "ModGuid", "ModGUID", "PluginGuid", "OwnerGuid", "SourceMod");
        return guid is not null && !string.IsNullOrWhiteSpace(guid.ToString());
    }

    private void LogDiscoveryOnce(string summary)
    {
        if (string.Equals(_lastDiscoverySummary, summary, StringComparison.Ordinal)) return;
        _lastDiscoverySummary = summary;
        log.LogInfo(summary);
    }

    private static Type? FindTypeBySimpleName(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? direct = null;
            try { direct = assembly.GetType(name, false, false); } catch { }
            if (direct is not null) return direct;
            foreach (var type in SafeGetTypes(assembly))
                if (string.Equals(type.Name, name, StringComparison.Ordinal)) return type;
        }
        return null;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).Cast<Type>(); }
        catch { return Array.Empty<Type>(); }
    }

    private static object? ReadStaticFirst(Type type, params string[] names)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        foreach (var name in names)
        {
            var p = type.GetProperty(name, flags);
            if (p?.GetMethod is not null)
            {
                try { return p.GetValue(null); } catch { }
            }
            var f = type.GetField(name, flags);
            if (f is not null)
            {
                try { return f.GetValue(null); } catch { }
            }
        }
        return null;
    }

    private static object? ReadFirst(object obj, Type type, params string[] names)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        foreach (var name in names)
        {
            var p = type.GetProperty(name, flags);
            if (p?.GetMethod is not null)
            {
                try { return p.GetValue(p.GetMethod.IsStatic ? null : obj); } catch { }
            }
            var f = type.GetField(name, flags);
            if (f is not null)
            {
                try { return f.GetValue(f.IsStatic ? null : obj); } catch { }
            }
        }
        return null;
    }

    private static bool ReadBool(object obj, Type type, params string[] names)
        => ReadFirst(obj, type, names) is bool b && b;

    private static bool? ReadNullableBool(object obj, Type type, params string[] names)
        => ReadFirst(obj, type, names) is bool b ? b : null;
}
