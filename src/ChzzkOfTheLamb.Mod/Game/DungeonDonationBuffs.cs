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
    private static float _gameplayClock;
    private static bool _clockInitialized;

    public static void Reset()
    {
        MoveQueue.Clear();
        AttackQueue.Clear();
        _gameplayClock = 0f;
        _clockInitialized = true;
    }

    /// <summary>
    /// Advances only while donation gameplay is safe. Loading, scene transitions,
    /// dialogue/cutscenes and the pause menu therefore consume no buff duration.
    /// </summary>
    public static void Tick(bool paused)
    {
        if (!_clockInitialized)
        {
            Reset();
            return;
        }

        if (!paused)
            _gameplayClock += Math.Max(0f, Time.unscaledDeltaTime);
    }

    public static string QueueMove(float multiplier, float seconds)
        => QueueGroup(multiplier, seconds, null, 0f);

    public static string QueueAttack(float multiplier, float seconds)
        => QueueGroup(null, 0f, multiplier, seconds);

    public static string QueueMoveAndAttack(float moveMultiplier, float attackMultiplier, float seconds)
        => QueueGroup(moveMultiplier, seconds, attackMultiplier, seconds);

    private static string QueueGroup(
        float? moveMultiplier,
        float moveSeconds,
        float? attackMultiplier,
        float attackSeconds)
    {
        var now = _gameplayClock;
        if (moveMultiplier.HasValue) PruneExpired(MoveQueue, now);
        if (attackMultiplier.HasValue) PruneExpired(AttackQueue, now);

        // Every timed effect produced by one donation is one scheduling group. The
        // group waits until every participating effect lane is free, then all lanes
        // begin on the exact same gameplay-clock tick.
        var startsAt = now;
        if (moveMultiplier.HasValue && MoveQueue.Count > 0)
            startsAt = Math.Max(startsAt, MoveQueue[MoveQueue.Count - 1].EndsAt);
        if (attackMultiplier.HasValue && AttackQueue.Count > 0)
            startsAt = Math.Max(startsAt, AttackQueue[AttackQueue.Count - 1].EndsAt);

        var details = new List<string>();
        if (moveMultiplier.HasValue)
        {
            var duration = Math.Max(0.1f, moveSeconds);
            MoveQueue.Add(new ScheduledBuff
            {
                Multiplier = moveMultiplier.Value,
                StartsAt = startsAt,
                EndsAt = startsAt + duration
            });
            details.Add($"movement x{moveMultiplier.Value:0.##} duration={duration:0}s queuePosition={Math.Max(0, MoveQueue.Count - 1)}");
        }

        if (attackMultiplier.HasValue)
        {
            var duration = Math.Max(0.1f, attackSeconds);
            AttackQueue.Add(new ScheduledBuff
            {
                Multiplier = attackMultiplier.Value,
                StartsAt = startsAt,
                EndsAt = startsAt + duration
            });
            details.Add($"attack x{attackMultiplier.Value:0.##} duration={duration:0}s queuePosition={Math.Max(0, AttackQueue.Count - 1)}");
        }

        var delay = Math.Max(0f, startsAt - now);
        var state = delay <= 0.01f ? "active" : "queued";
        return $"buffGroup={state}; sharedStartIn={delay:0.0}s; {string.Join("; ", details)}";
    }

    public static float ApplyMove(float value)
        => ApplyScheduled(value, MoveQueue);

    public static float ApplyAttack(float value)
        => ApplyScheduled(value, AttackQueue);

    private static float ApplyScheduled(float value, List<ScheduledBuff> queue)
    {
        var now = _gameplayClock;
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
