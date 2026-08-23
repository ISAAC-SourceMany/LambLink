using System;
using System.Reflection;
using UnityEngine.SceneManagement;

namespace ChzzkOfTheLamb.Mod.Game;

internal static class DungeonContext
{
    private static readonly BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool IsDungeon(out string evidence)
    {
        try
        {
            if (DungeonSandboxManager.Active)
            {
                evidence = "DungeonSandboxManager.Active=true";
                return true;
            }
        }
        catch { }

        try
        {
            var scene = SceneManager.GetActiveScene().name ?? string.Empty;
            if (scene.StartsWith("Dungeon", StringComparison.OrdinalIgnoreCase))
            {
                evidence = $"scene={scene}";
                return true;
            }
        }
        catch { }

        try
        {
            var type = typeof(PlayerFarming);
            object? value = type.GetProperty("Location", StaticAny)?.GetValue(null, null)
                            ?? type.GetField("Location", StaticAny)?.GetValue(null);
            var text = value?.ToString() ?? string.Empty;
            if (text.StartsWith("Dungeon", StringComparison.OrdinalIgnoreCase))
            {
                evidence = $"PlayerFarming.Location={text}";
                return true;
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                evidence = $"PlayerFarming.Location={text}";
                return false;
            }
        }
        catch { }

        try
        {
            var scene = SceneManager.GetActiveScene().name ?? string.Empty;
            evidence = string.IsNullOrWhiteSpace(scene) ? "no dungeon evidence" : $"scene={scene}";
        }
        catch
        {
            evidence = "no dungeon evidence";
        }
        return false;
    }

    public static string GetArea(out string evidence)
    {
        if (PlayerFarming.Instance == null)
        {
            evidence = "PlayerFarming.Instance=null";
            return "UNKNOWN";
        }
        return IsDungeon(out evidence) ? "DUNGEON" : "BASE";
    }
}
