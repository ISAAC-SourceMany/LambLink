using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ChzzkOfTheLamb.Mod.Game;

internal static class DungeonDonationBuffState
{
    private sealed class ScheduledBuff
    {
        public float Multiplier { get; set; }
        public float StartsAt { get; set; }
        public float EndsAt { get; set; }
    }

    private static readonly List<ScheduledBuff> MoveQueue = new();
    private static readonly List<ScheduledBuff> AttackQueue = new();

    public static string QueueMove(float multiplier, float seconds)
        => Queue(MoveQueue, "movement", multiplier, seconds);

    public static string QueueAttack(float multiplier, float seconds)
        => Queue(AttackQueue, "attack", multiplier, seconds);

    private static string Queue(List<ScheduledBuff> queue, string key, float multiplier, float seconds)
    {
        var now = Time.unscaledTime;
        PruneExpired(queue, now);

        var startsAt = queue.Count == 0 ? now : Math.Max(now, queue[queue.Count - 1].EndsAt);
        var endsAt = startsAt + Math.Max(0.1f, seconds);
        queue.Add(new ScheduledBuff
        {
            Multiplier = multiplier,
            StartsAt = startsAt,
            EndsAt = endsAt
        });

        var delay = Math.Max(0f, startsAt - now);
        var position = Math.Max(0, queue.Count - 1);
        return delay <= 0.01f
            ? $"{key} x{multiplier:0.##} active for {seconds:0}s; queuedBehind=0"
            : $"{key} x{multiplier:0.##} queued; startsIn={delay:0.0}s; duration={seconds:0}s; queuePosition={position}";
    }

    public static float ApplyMove(float value)
        => ApplyScheduled(value, MoveQueue);

    public static float ApplyAttack(float value)
        => ApplyScheduled(value, AttackQueue);

    private static float ApplyScheduled(float value, List<ScheduledBuff> queue)
    {
        var now = Time.unscaledTime;
        PruneExpired(queue, now);
        if (queue.Count == 0) return value;

        // Queue entries are scheduled back-to-back when donations arrive. Only the
        // single entry whose time window is active may affect gameplay.
        var active = queue[0];
        if (now < active.StartsAt || now >= active.EndsAt)
            return value;

        return value * active.Multiplier;
    }

    private static void PruneExpired(List<ScheduledBuff> queue, float now)
    {
        while (queue.Count > 0 && now >= queue[0].EndsAt)
            queue.RemoveAt(0);
    }
}

[HarmonyPatch(typeof(PlayerController), "GetPlayerMaxSpeed")]
internal static class ChzzkDungeonMoveSpeedPatch
{
    private static void Postfix(ref float __result)
    {
        if (DungeonContext.IsDungeon(out _))
            __result = DungeonDonationBuffState.ApplyMove(__result);
    }
}

[HarmonyPatch(typeof(PlayerWeapon), "GetDamage", new Type[] { typeof(float), typeof(int), typeof(PlayerFarming) })]
internal static class ChzzkDungeonAttackDamagePatch
{
    private static void Postfix(ref float __result)
    {
        if (DungeonContext.IsDungeon(out _))
            __result = DungeonDonationBuffState.ApplyAttack(__result);
    }
}
