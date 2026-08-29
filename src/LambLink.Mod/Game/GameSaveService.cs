using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;

namespace LambLink.Mod.Game;

/// <summary>
/// Resolves the active Cult of the Lamb save slot without hard-coding one game-build field.
/// Runtime member discovery is preferred; filesystem inference is only a fallback.
/// </summary>
public sealed class GameSaveService(ManualLogSource log)
{
    private string _lastResolved = "unknown";
    private string? _lastSource;
    private float _nextProbeAt;

    // This value is populated only by the Unity/main-thread resolver, but can be read safely by
    // the bridge receive thread to provide a non-Unity fallback GAME_STATUS response.
    public string LastResolvedSaveId => Volatile.Read(ref _lastResolved);

    public string GetCurrentSaveId()
    {
        // Save state does not change every frame. Cache briefly to keep reflection cheap.
        if (Time.unscaledTime < _nextProbeAt && _lastResolved != "unknown")
            return _lastResolved;

        _nextProbeAt = Time.unscaledTime + 1f;

        try
        {
            if (TryResolveFromGameState(out var saveId, out var source))
            {
                Remember(saveId, source);
                return saveId;
            }

            if (TryResolveFromSaveDirectory(out saveId, out source))
            {
                Remember(saveId, source);
                return saveId;
            }
        }
        catch (Exception ex)
        {
            log.LogDebug($"Save ID discovery unavailable: {ex.Message}");
        }

        return _lastResolved;
    }

    private bool TryResolveFromGameState(out string saveId, out string source)
    {
        saveId = "unknown";
        source = string.Empty;

        var dataManagerType = FindTypeBySimpleName("DataManager");
        if (dataManagerType is null) return false;

        var instance = ReadStaticMember(dataManagerType, "Instance", "_Instance", "instance");

        // Known/common names first.
        var preferredNames = new[]
        {
            "SaveSlot", "CurrentSaveSlot", "CurrentSaveSlotIndex", "SaveFileIndex", "CurrentSaveFileIndex",
            "Slot", "CurrentSlot", "SaveIndex", "CurrentSave", "CurrentSaveFile", "LoadedSaveSlot"
        };

        foreach (var name in preferredNames)
        {
            if (TryReadNamedMember(dataManagerType, instance, name, out var value)
                && TryNormalizeSlot(value, out saveId))
            {
                source = $"{dataManagerType.Name}.{name}";
                return true;
            }
        }

        // Game updates sometimes rename slot fields. Inspect likely primitive members instead
        // of failing permanently on one field-name change.
        const BindingFlags instanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var member in dataManagerType.GetMembers(instanceFlags)
                     .Where(m => m.Name.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0
                              || m.Name.IndexOf("save", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            object? value;
            try { value = ReadMember(instance, member); }
            catch { continue; }

            if (TryNormalizeSlot(value, out saveId))
            {
                source = $"{dataManagerType.Name}.{member.Name}";
                return true;
            }
        }

        // A few builds keep save bookkeeping on another singleton/static helper.
        foreach (var type in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
                     .SelectMany(SafeGetTypes)
                     .Where(t => t.Name.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                         .Where(m => m.Name.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                object? value;
                try { value = ReadMember(null, member); }
                catch { continue; }
                if (!TryNormalizeSlot(value, out saveId)) continue;
                source = $"{type.Name}.{member.Name}";
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveFromSaveDirectory(out string saveId, out string source)
    {
        saveId = "unknown";
        source = string.Empty;

        var saveDirectory = Path.Combine(Application.persistentDataPath, "saves");
        if (!Directory.Exists(saveDirectory)) return false;

        // Only base save files are candidates. Ignore meta_*.mp and mod-owned *_slot_*.mp files.
        var candidates = Directory.GetFiles(saveDirectory, "slot_*.mp", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(f => TryParseSlotFromFileName(f.Name, out _))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        if (candidates.Count == 0) return false;

        // If there is one real save, it is unambiguous (this is also a common early-game state).
        if (candidates.Count == 1 && TryParseSlotFromFileName(candidates[0].Name, out saveId))
        {
            source = "save-directory(single-slot)";
            return true;
        }

        // While actually in a loaded game, the most recently touched base slot is a useful final
        // fallback. We deliberately avoid this inference on the main menu where choosing the wrong
        // slot would be worse than returning unknown.
        if (PlayerFarming.Instance != null && TryParseSlotFromFileName(candidates[0].Name, out saveId))
        {
            source = "save-directory(recent-loaded-slot)";
            return true;
        }

        return false;
    }

    private void Remember(string saveId, string source)
    {
        if (saveId == "unknown") return;
        if (!string.Equals(_lastResolved, saveId, StringComparison.Ordinal)
            || !string.Equals(_lastSource, source, StringComparison.Ordinal))
        {
            log.LogInfo($"Resolved active save: {saveId} via {source}");
            _lastResolved = saveId;
            _lastSource = source;
        }
    }

    private static bool TryReadNamedMember(Type type, object? instance, string name, out object? value)
    {
        value = null;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var p = type.GetProperty(name, all);
        if (p is not null)
        {
            try { value = p.GetValue(p.GetMethod?.IsStatic == true ? null : instance); return value is not null; }
            catch { }
        }
        var f = type.GetField(name, all);
        if (f is not null)
        {
            try { value = f.GetValue(f.IsStatic ? null : instance); return value is not null; }
            catch { }
        }
        return false;
    }

    private static bool TryNormalizeSlot(object? value, out string saveId)
    {
        saveId = "unknown";
        if (value is null) return false;

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
        {
            try
            {
                var index = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (index >= 0 && index <= 99)
                {
                    saveId = $"slot_{index}";
                    return true;
                }
            }
            catch { return false; }
        }

        if (value.GetType().IsEnum)
        {
            try
            {
                var index = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (index >= 0 && index <= 99)
                {
                    saveId = $"slot_{index}";
                    return true;
                }
            }
            catch { }
        }

        if (value is string text)
        {
            text = text.Trim();
            if (text.Length == 0) return false;
            if (TryExtractSlotToken(text, out saveId)) return true;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index <= 99)
            {
                saveId = $"slot_{index}";
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractSlotToken(string text, out string saveId)
    {
        saveId = "unknown";
        var lower = text.ToLowerInvariant();
        var pos = lower.IndexOf("slot_", StringComparison.Ordinal);
        if (pos < 0) pos = lower.IndexOf("slot", StringComparison.Ordinal);
        if (pos < 0) return false;

        var digits = new string(lower.Skip(pos).SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var index) || index < 0 || index > 99) return false;
        saveId = $"slot_{index}";
        return true;
    }

    private static bool TryParseSlotFromFileName(string fileName, out string saveId)
    {
        saveId = "unknown";
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (!name.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)) return false;
        return TryExtractSlotToken(name, out saveId);
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

    private static object? ReadStaticMember(Type type, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryReadNamedMember(type, null, name, out var value)) return value;
        }
        return null;
    }

    private static object? ReadMember(object? instance, MemberInfo member) => member switch
    {
        FieldInfo f => f.GetValue(f.IsStatic ? null : instance),
        PropertyInfo p when p.GetMethod is not null => p.GetValue(p.GetMethod.IsStatic ? null : instance),
        _ => null
    };
}
