using System;
using HarmonyLib;
using UnityEngine;

namespace ChzzkOfTheLamb.Mod.Game;

internal static class DungeonDonationBuffState
{
    private static float _moveMultiplier = 1f;
    private static float _moveUntil;
    private static float _attackMultiplier = 1f;
    private static float _attackUntil;

    public static void ActivateMove(float multiplier, float seconds)
    {
        _moveMultiplier = Math.Max(_moveMultiplier, multiplier);
        _moveUntil = Math.Max(_moveUntil, Time.unscaledTime + seconds);
    }

    public static void ActivateAttack(float multiplier, float seconds)
    {
        _attackMultiplier = Math.Max(_attackMultiplier, multiplier);
        _attackUntil = Math.Max(_attackUntil, Time.unscaledTime + seconds);
    }

    public static float ApplyMove(float value)
    {
        if (Time.unscaledTime >= _moveUntil)
        {
            _moveMultiplier = 1f;
            return value;
        }
        return value * _moveMultiplier;
    }

    public static float ApplyAttack(float value)
    {
        if (Time.unscaledTime >= _attackUntil)
        {
            _attackMultiplier = 1f;
            return value;
        }
        return value * _attackMultiplier;
    }
}

[HarmonyPatch(typeof(PlayerController), "GetPlayerMaxSpeed")]
internal static class ChzzkDungeonMoveSpeedPatch
{
    private static void Postfix(ref float __result)
    {
        if (DungeonSandboxManager.Active)
            __result = DungeonDonationBuffState.ApplyMove(__result);
    }
}

[HarmonyPatch(typeof(PlayerWeapon), "GetDamage", new Type[] { typeof(float), typeof(int), typeof(PlayerFarming) })]
internal static class ChzzkDungeonAttackDamagePatch
{
    private static void Postfix(ref float __result)
    {
        if (DungeonSandboxManager.Active)
            __result = DungeonDonationBuffState.ApplyAttack(__result);
    }
}
