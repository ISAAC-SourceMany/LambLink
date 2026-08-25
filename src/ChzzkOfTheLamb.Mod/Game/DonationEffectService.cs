using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ChzzkOfTheLamb.Protocol;
using Newtonsoft.Json;
using UnityEngine;

namespace ChzzkOfTheLamb.Mod.Game;

public sealed class DonationEffectService
{
    private readonly ManualLogSource _log;
    private bool _capabilitiesLogged;

    public DonationEffectService(ManualLogSource log)
    {
        _log = log;
        LogDungeonCapabilities();
    }

    public static string GetCurrentArea()
    {
        return DungeonContext.GetArea(out _);
    }

    public DonationEffectResult ApplyFromJson(string payloadJson)
    {
        DonationEffectCommand? command = null;
        var started = Stopwatch.GetTimestamp();
        var stage = "DESERIALIZE";
        try
        {
            command = JsonConvert.DeserializeObject<DonationEffectCommand>(payloadJson)
                      ?? throw new InvalidOperationException("Invalid DonationEffect command.");

            stage = "CONTEXT";
            var actualArea = DungeonContext.GetArea(out var areaEvidence);
            _log.LogInfo($"[DONATION][RX] request={Short(command.RequestId)}, amount={command.Amount}, effect={command.Effect}, event='{command.EventName}', messageChars={command.Message?.Length ?? 0}, actualArea={actualArea}, evidence={areaEvidence}");

            stage = "READY-CHECK";
            if (PlayerFarming.Instance == null || DataManager.Instance == null)
                throw new InvalidOperationException("game is not ready");

            // The Companion receives GAME_STATUS every few seconds, so a donation can arrive during a
            // scene transition with a stale area hint. The Mod is authoritative and corrects the event
            // to the actual runtime area before applying it.
            if (actualArea == "DUNGEON" && !command.Effect.StartsWith("DUNGEON_", StringComparison.Ordinal))
            {
                var corrected = PickDungeonEvent(command.Amount);
                _log.LogInfo($"[DONATION][CONTEXT] request={Short(command.RequestId)} stale Companion area corrected: {command.Effect} -> {corrected.Effect}");
                command.Effect = corrected.Effect;
                command.EventName = corrected.Name;
            }
            else if (actualArea != "DUNGEON" && command.Effect.StartsWith("DUNGEON_", StringComparison.Ordinal))
            {
                var corrected = PickBaseFallback(command.Amount);
                _log.LogInfo($"[DONATION][CONTEXT] request={Short(command.RequestId)} dungeon ended before apply; fallback: {command.Effect} -> {corrected.Effect}");
                command.Effect = corrected.Effect;
                command.EventName = corrected.Name;
            }

            stage = "APPLY";
            var details = ApplyExact(command);
            stage = "RESULT";
            _log.LogInfo($"[DONATION][APPLIED] request={Short(command.RequestId)}, elapsedMs={ElapsedMilliseconds(started):F1}, area={actualArea}, event='{command.EventName}', effect={command.Effect}, details={details}");

            return new DonationEffectResult
            {
                RequestId = command.RequestId,
                Success = true,
                Effect = command.Effect,
                EventName = command.EventName,
                Amount = command.Amount,
                Nickname = command.Nickname,
                Details = $"area={actualArea}; {details}"
            };
        }
        catch (Exception ex)
        {
            var root = ex.GetBaseException();
            _log.LogWarning($"[DONATION][FAILED] request={Short(command?.RequestId)}, elapsedMs={ElapsedMilliseconds(started):F1}, stage={stage}, effect={command?.Effect ?? "unknown"}, type={root.GetType().FullName}: {root.Message}");
            return new DonationEffectResult
            {
                RequestId = command?.RequestId ?? string.Empty,
                Success = false,
                Effect = command?.Effect ?? string.Empty,
                EventName = command?.EventName ?? string.Empty,
                Amount = command?.Amount ?? 0,
                Nickname = command?.Nickname ?? string.Empty,
                Error = root.Message
            };
        }
    }

    private string ApplyExact(DonationEffectCommand command)
    {
        return command.Effect switch
        {
            // Village/base effects.
            "SMALL_FAITH_UP_5" => Faith(+5f, command, "faith +5"),
            "SMALL_FAITH_DOWN_5" => Faith(-5f, command, "faith -5"),
            "SMALL_RANDOM_FOLLOWER_FOOD_UP_15" => RandomFollowerFood(+15f),
            "SMALL_ALL_FOOD_UP_5" => AllFollowerFood(+5f),
            "SMALL_BALANCED_UP_3" => FaithAndFood(+3f, +3f, command),
            "MEDIUM_FAITH_UP_10" => Faith(+10f, command, "faith +10"),
            "MEDIUM_FAITH_DOWN_10" => Faith(-10f, command, "faith -10"),
            "MEDIUM_ALL_FOOD_UP_10" => AllFollowerFood(+10f),
            "MEDIUM_ALL_FOOD_DOWN_10" => AllFollowerFood(-10f),
            "MEDIUM_BALANCED_UP_7" => FaithAndFood(+7f, +7f, command),
            "MEDIUM_BALANCED_DOWN_7" => FaithAndFood(-7f, -7f, command),
            "HH_FAITH_UP_20" => Faith(+20f, command, "faith +20"),
            "HH_ALL_FOOD_UP_20" => AllFollowerFood(+20f),
            "HH_BALANCED_UP_15" => FaithAndFood(+15f, +15f, command),
            "HH_FAITH_DOWN_15" => Faith(-15f, command, "faith -15"),
            "HH_ALL_FOOD_DOWN_15" => AllFollowerFood(-15f),
            "HH_BALANCED_DOWN_10" => FaithAndFood(-10f, -10f, command),
            "SPECIAL_FULL_FEAST" => FullFeast(),
            "SPECIAL_FAITH_UP_30" => Faith(+30f, command, "faith +30"),
            "SPECIAL_BALANCED_UP_25" => FaithAndFood(+25f, +25f, command),
            "SPECIAL_FAITH_DOWN_20" => Faith(-20f, command, "faith -20"),
            "SPECIAL_BALANCED_DOWN_20" => FaithAndFood(-20f, -20f, command),
            "SPECIAL_RANDOM_FOLLOWER_FEAST_FAITH_20" => ChosenFollowerBlessing(command),

            // Dungeon-only effects. These use only members verified in the user's
            // Cult Of The Lamb 1.5.25.1049 Assembly-CSharp.dll.
            "DUNGEON_HEAL_SMALL" => HealPlayerHearts(0.5f),
            "DUNGEON_HURT_SMALL" => HurtPlayerNonLethalHearts(0.5f),
            "DUNGEON_FERVOUR_SMALL" => ChangeFervour(0.20f, false),
            "DUNGEON_SPEED_SMALL" => BuffMove(1.15f, 8f),
            "DUNGEON_SPEED_DOWN_SMALL" => BuffMove(0.85f, 8f),
            "DUNGEON_ENEMY_DAMAGE_SMALL" => DamageAllEnemies(0.5f),

            "DUNGEON_HEAL_MEDIUM" => HealPlayerHearts(1.0f),
            "DUNGEON_HURT_MEDIUM" => HurtPlayerNonLethalHearts(1.0f),
            "DUNGEON_FERVOUR_MEDIUM" => ChangeFervour(0.35f, false),
            "DUNGEON_SPEED_MEDIUM" => BuffMove(1.20f, 10f),
            "DUNGEON_ATTACK_MEDIUM" => BuffAttack(1.20f, 10f),
            "DUNGEON_SPEED_DOWN_MEDIUM" => BuffMove(0.80f, 10f),
            "DUNGEON_ATTACK_DOWN_MEDIUM" => BuffAttack(0.80f, 10f),
            "DUNGEON_ENEMY_DAMAGE_MEDIUM" => DamageAllEnemies(1.0f),

            "DUNGEON_HEAL_LARGE" => HealPlayerHearts(1.5f),
            "DUNGEON_HURT_LARGE" => HurtPlayerNonLethalHearts(1.5f),
            "DUNGEON_FERVOUR_LARGE" => ChangeFervour(0.50f, false),
            "DUNGEON_SPEED_ATTACK_LARGE" => BuffMoveAndAttack(1.25f, 1.25f, 12f),
            "DUNGEON_SPEED_ATTACK_DOWN_LARGE" => BuffMoveAndAttack(0.75f, 0.75f, 12f),
            "DUNGEON_ENEMY_DAMAGE_LARGE" => DamageAllEnemies(1.5f),

            "DUNGEON_HEAL_SPECIAL" => HealPlayerHearts(2.0f),
            "DUNGEON_HURT_SPECIAL" => HurtPlayerNonLethalHearts(2.0f),
            "DUNGEON_FERVOUR_SPECIAL" => ChangeFervour(1f, true),
            "DUNGEON_SPEED_ATTACK_SPECIAL" => BuffMoveAndAttack(1.40f, 1.40f, 15f),
            "DUNGEON_SPEED_ATTACK_DOWN_SPECIAL" => BuffMoveAndAttack(0.60f, 0.60f, 15f),
            "DUNGEON_ENEMY_DAMAGE_SPECIAL" => DamageAllEnemies(2.5f),

            "SMALL_RANDOM" => ApplyLegacySmall(command),
            "MEDIUM_RANDOM" => ApplyLegacyMedium(command),
            "HELP_OR_HINDER_RANDOM" => ApplyLegacyHelpOrHinder(command),
            "SPECIAL_RANDOM" => ApplyLegacySpecial(command),
            _ => throw new InvalidOperationException($"Unknown donation effect: {command.Effect}")
        };
    }

    // Cult of the Lamb stores combat HP in half-heart units: 2 raw HP == 1 HUD heart.
    // Donation rules are expressed in visible HUD hearts, so convert at the boundary.
    private const float RawHpPerHeart = 2f;

    private static float RawHpToHearts(float rawHp) => rawHp / RawHpPerHeart;
    private static float HeartsToRawHp(float hearts) => hearts * RawHpPerHeart;

    private string HealPlayerHearts(float hearts)
    {
        EnsureDungeon();
        var health = PlayerFarming.Instance.health ?? throw new InvalidOperationException("PlayerFarming.Instance.health is null");
        var beforeRaw = health.HP;
        var rawAmount = HeartsToRawHp(hearts);
        health.Heal(rawAmount);
        var afterRaw = health.HP;
        return $"player HP raw {beforeRaw:0.##} -> {afterRaw:0.##}; HUD hearts {RawHpToHearts(beforeRaw):0.##} -> {RawHpToHearts(afterRaw):0.##}; requested +{hearts:0.##} heart(s) (HealthPlayer.Heal({rawAmount:0.##} raw))";
    }

    private string HurtPlayerNonLethalHearts(float hearts)
    {
        EnsureDungeon();
        var health = PlayerFarming.Instance.health ?? throw new InvalidOperationException("PlayerFarming.Instance.health is null");
        var beforeRaw = health.HP;
        var rawAmount = HeartsToRawHp(hearts);
        // Keep at least half a visible heart. This continues to avoid donation-triggered death/soft-locks.
        var minimumRawHp = HeartsToRawHp(0.5f);
        health.HP = Mathf.Max(minimumRawHp, beforeRaw - rawAmount);
        var afterRaw = health.HP;
        return $"player HP raw {beforeRaw:0.##} -> {afterRaw:0.##}; HUD hearts {RawHpToHearts(beforeRaw):0.##} -> {RawHpToHearts(afterRaw):0.##}; requested -{hearts:0.##} heart(s) (non-lethal, min 0.5 heart)";
    }

    private string ChangeFervour(float fractionOfTotal, bool fill)
    {
        EnsureDungeon();
        var spells = PlayerFarming.Instance.playerSpells ?? throw new InvalidOperationException("PlayerFarming.Instance.playerSpells is null");
        var (ammo, memberName) = ResolveFervourAmmo(spells);

        var before = ammo.Ammo;
        var total = ammo.Total;
        ammo.Ammo = fill ? total : Mathf.Clamp(before + total * fractionOfTotal, 0f, total);
        return $"fervour {before:0.##}/{total:0.##} -> {ammo.Ammo:0.##}/{total:0.##} (resolved via {memberName}; FaithAmmo.Ammo)";
    }

    private static (FaithAmmo Ammo, string MemberName) ResolveFervourAmmo(PlayerSpells spells)
    {
        // Resolve by the verified runtime type instead of guessing a private member name.
        // This survives game builds where the backing field/property name changes.
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var property in typeof(PlayerSpells).GetProperties(flags))
        {
            if (!typeof(FaithAmmo).IsAssignableFrom(property.PropertyType) || !property.CanRead) continue;
            try
            {
                if (property.GetValue(spells, null) is FaithAmmo ammo)
                    return (ammo, $"property PlayerSpells.{property.Name}");
            }
            catch
            {
                // Ignore an inaccessible/throwing property and continue to the next verified member.
            }
        }

        foreach (var field in typeof(PlayerSpells).GetFields(flags))
        {
            if (!typeof(FaithAmmo).IsAssignableFrom(field.FieldType)) continue;
            if (field.GetValue(spells) is FaithAmmo ammo)
                return (ammo, $"field PlayerSpells.{field.Name}");
        }

        throw new MissingMemberException(typeof(PlayerSpells).FullName, "FaithAmmo-typed field/property");
    }

    private string BuffMove(float multiplier, float seconds)
    {
        EnsureDungeon();
        var queue = DungeonDonationBuffState.QueueMove(multiplier, seconds);
        return $"{queue} (PlayerController.GetPlayerMaxSpeed postfix)";
    }

    private string BuffAttack(float multiplier, float seconds)
    {
        EnsureDungeon();
        var queue = DungeonDonationBuffState.QueueAttack(multiplier, seconds);
        return $"{queue} (PlayerWeapon.GetDamage postfix)";
    }

    private string BuffMoveAndAttack(float moveMultiplier, float attackMultiplier, float seconds)
    {
        EnsureDungeon();
        var moveQueue = DungeonDonationBuffState.QueueMove(moveMultiplier, seconds);
        var attackQueue = DungeonDonationBuffState.QueueAttack(attackMultiplier, seconds);
        return $"{moveQueue}; {attackQueue}";
    }

    private string DamageAllEnemies(float damage)
    {
        EnsureDungeon();

        // Do not bind to DamageAllEnemiesType at compile time. In the game assembly used by
        // this project the nested/qualified enum name is not exposed through the NuGet compile
        // reference, even though the runtime Assembly-CSharp metadata contains both
        // Health.DamageAllEnemies and the enum member Manipulation. Resolve the exact loaded
        // signature from Health at runtime and invoke only after validating it.
        var candidate = typeof(Health)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "DamageAllEnemies", StringComparison.Ordinal)) return false;
                var parameters = method.GetParameters();
                if (parameters.Length != 2 || parameters[0].ParameterType != typeof(float)) return false;
                var modeType = parameters[1].ParameterType;
                return modeType.IsEnum && Enum.GetNames(modeType).Contains("Manipulation");
            });

        if (candidate == null)
            throw new MissingMethodException(typeof(Health).FullName, "DamageAllEnemies(float, enum-with-Manipulation)");

        var modeParameterType = candidate.GetParameters()[1].ParameterType;
        var manipulation = Enum.Parse(modeParameterType, "Manipulation");
        candidate.Invoke(null, new[] { (object)damage, manipulation });

        return $"all current enemies damage {damage:0.##} ({typeof(Health).FullName}.{candidate.Name}, mode={modeParameterType.FullName}.Manipulation)";
    }

    private static void EnsureDungeon()
    {
        if (!DungeonContext.IsDungeon(out var evidence))
            throw new InvalidOperationException($"dungeon-only donation effect requested outside dungeon ({evidence})");
    }

    private void LogDungeonCapabilities()
    {
        if (_capabilitiesLogged) return;
        _capabilitiesLogged = true;

        var heal = typeof(HealthPlayer).GetMethod("Heal", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float) }, null) != null;
        var hp = typeof(HealthPlayer).GetProperty("HP", BindingFlags.Instance | BindingFlags.Public) is { CanRead: true, CanWrite: true };
        var playerSpellsFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fervourMember = typeof(PlayerSpells).GetProperties(playerSpellsFlags)
            .FirstOrDefault(p => p.CanRead && typeof(FaithAmmo).IsAssignableFrom(p.PropertyType))?.Name
            ?? typeof(PlayerSpells).GetFields(playerSpellsFlags)
                .FirstOrDefault(f => typeof(FaithAmmo).IsAssignableFrom(f.FieldType))?.Name;
        var fervourMemberAvailable = !string.IsNullOrWhiteSpace(fervourMember);
        var fervourAmmo = typeof(FaithAmmo).GetProperty("Ammo", BindingFlags.Instance | BindingFlags.Public) is { CanRead: true, CanWrite: true };
        var fervourTotal = typeof(FaithAmmo).GetProperty("Total", BindingFlags.Instance | BindingFlags.Public) is { CanRead: true };
        var speed = typeof(PlayerController).GetMethod("GetPlayerMaxSpeed", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null) != null;
        var attack = typeof(PlayerWeapon).GetMethod("GetDamage", BindingFlags.Static | BindingFlags.Public, null,
            new[] { typeof(float), typeof(int), typeof(PlayerFarming) }, null) != null;
        var enemyDamageMethod = typeof(Health)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "DamageAllEnemies", StringComparison.Ordinal)) return false;
                var parameters = method.GetParameters();
                if (parameters.Length != 2 || parameters[0].ParameterType != typeof(float)) return false;
                var modeType = parameters[1].ParameterType;
                return modeType.IsEnum && Enum.GetNames(modeType).Contains("Manipulation");
            });
        var enemyDamage = enemyDamageMethod != null;
        var dungeonActive = typeof(DungeonSandboxManager).GetProperty("Active", BindingFlags.Static | BindingFlags.Public) is { CanRead: true };

        var enemyDamageSignature = enemyDamageMethod == null
            ? "UNAVAILABLE"
            : $"{enemyDamageMethod.DeclaringType?.FullName}.{enemyDamageMethod.Name}({string.Join(", ", enemyDamageMethod.GetParameters().Select(p => p.ParameterType.FullName))})";

        _log.LogInfo($"[DONATION][CAPABILITY] verified against runtime types: dungeonActive={dungeonActive}, playerHeal={heal}, playerHP={hp}, hpScaleRawPerHeart={RawHpPerHeart:0.##}, fervourMember={fervourMember ?? "UNAVAILABLE"}, fervourAmmo={fervourAmmo}, fervourTotal={fervourTotal}, moveSpeed={speed}, attackDamage={attack}, enemyDamage={enemyDamage}");
        _log.LogInfo($"[DONATION][CAPABILITY] enemyDamageSignature={enemyDamageSignature}; Manipulation enum member validated={enemyDamage}");
        if (!(dungeonActive && heal && hp && fervourMemberAvailable && fervourAmmo && fervourTotal && speed && attack && enemyDamage))
            _log.LogWarning("[DONATION][CAPABILITY] one or more dungeon donation members are unavailable; affected effects will fail safely and return DONATION_EFFECT_RESULT failure.");
    }

    private static (string Effect, string Name) PickDungeonEvent(long amount)
    {
        (string Effect, string Name)[] pool = amount switch
        {
            <= 2_999 => new[]
            {
                ("DUNGEON_HEAL_SMALL", "작은 치유"), ("DUNGEON_HURT_SMALL", "작은 시련"),
                ("DUNGEON_FERVOUR_SMALL", "열정 충전"), ("DUNGEON_SPEED_SMALL", "신속의 축복"),
                ("DUNGEON_SPEED_DOWN_SMALL", "둔화의 장난"), ("DUNGEON_ENEMY_DAMAGE_SMALL", "적을 향한 일격")
            },
            <= 4_999 => new[]
            {
                ("DUNGEON_HEAL_MEDIUM", "치유의 손길"), ("DUNGEON_HURT_MEDIUM", "고통의 장난"),
                ("DUNGEON_FERVOUR_MEDIUM", "열정의 샘"), ("DUNGEON_SPEED_MEDIUM", "질주의 축복"),
                ("DUNGEON_ATTACK_MEDIUM", "전투의 축복"), ("DUNGEON_SPEED_DOWN_MEDIUM", "무거운 발걸음"),
                ("DUNGEON_ATTACK_DOWN_MEDIUM", "무뎌진 칼날"), ("DUNGEON_ENEMY_DAMAGE_MEDIUM", "적 무리 강타")
            },
            <= 9_999 => new[]
            {
                ("DUNGEON_HEAL_LARGE", "강한 치유"), ("DUNGEON_HURT_LARGE", "강한 시련"),
                ("DUNGEON_FERVOUR_LARGE", "넘치는 열정"), ("DUNGEON_SPEED_ATTACK_LARGE", "광전사의 축복"),
                ("DUNGEON_SPEED_ATTACK_DOWN_LARGE", "쇠약의 저주"), ("DUNGEON_ENEMY_DAMAGE_LARGE", "적 무리 대타격")
            },
            _ => new[]
            {
                ("DUNGEON_HEAL_SPECIAL", "기적의 치유"), ("DUNGEON_HURT_SPECIAL", "신의 시련"),
                ("DUNGEON_FERVOUR_SPECIAL", "열정 완전 충전"), ("DUNGEON_SPEED_ATTACK_SPECIAL", "전투의 기적"),
                ("DUNGEON_SPEED_ATTACK_DOWN_SPECIAL", "전투의 대저주"), ("DUNGEON_ENEMY_DAMAGE_SPECIAL", "적 무리 대폭발")
            }
        };
        return pool[UnityEngine.Random.Range(0, pool.Length)];
    }

    private static (string Effect, string Name) PickBaseFallback(long amount) => amount switch
    {
        <= 2_999 => ("SMALL_RANDOM", "마을 소규모 후원"),
        <= 4_999 => ("MEDIUM_RANDOM", "마을 중규모 후원"),
        <= 9_999 => ("HELP_OR_HINDER_RANDOM", "마을 도움/방해 후원"),
        _ => ("SPECIAL_RANDOM", "마을 특별 후원")
    };

    private string Faith(float amount, DonationEffectCommand command, string details)
    {
        ChangeCultFaith(amount, $"CHZZK {command.Nickname} donation");
        return details;
    }

    private string AllFollowerFood(float amount)
    {
        var followers = DataManager.Instance.Followers;
        if (followers == null || followers.Count == 0)
            return "no followers; food effect had no targets";

        var changed = 0;
        foreach (var follower in followers)
        {
            if (follower == null) continue;
            follower.Satiation = Mathf.Clamp(follower.Satiation + amount, 0f, 100f);
            if (amount > 0f && follower.Satiation > 0f)
                follower.Starvation = Mathf.Max(0f, follower.Starvation - amount);
            changed++;
        }
        return $"all follower satiation {(amount >= 0 ? "+" : string.Empty)}{amount:0} (targets={changed})";
    }

    private string RandomFollowerFood(float amount)
    {
        if (!TryGetRandomFollower(out var follower))
            return "no followers; random food effect had no target";

        follower.Satiation = Mathf.Clamp(follower.Satiation + amount, 0f, 100f);
        if (amount > 0f)
            follower.Starvation = Mathf.Max(0f, follower.Starvation - amount);
        return $"random follower satiation {(amount >= 0 ? "+" : string.Empty)}{amount:0}";
    }

    private string FaithAndFood(float faith, float food, DonationEffectCommand command)
    {
        ChangeCultFaith(faith, $"CHZZK {command.Nickname} donation");
        var foodDetails = AllFollowerFood(food);
        return $"faith {(faith >= 0 ? "+" : string.Empty)}{faith:0}; {foodDetails}";
    }

    private string FullFeast()
    {
        var followers = DataManager.Instance.Followers;
        var changed = 0;
        if (followers != null)
        {
            foreach (var follower in followers)
            {
                if (follower == null) continue;
                follower.Satiation = 100f;
                follower.Starvation = 0f;
                changed++;
            }
        }
        return $"all followers fully fed (targets={changed})";
    }

    private string ChosenFollowerBlessing(DonationEffectCommand command)
    {
        ChangeCultFaith(+20f, $"CHZZK SPECIAL by {command.Nickname}");
        if (!TryGetRandomFollower(out var follower))
            return "faith +20; no follower available for feast";

        follower.Satiation = 100f;
        follower.Starvation = 0f;
        return "faith +20; selected follower fully fed";
    }

    private string ApplyLegacySmall(DonationEffectCommand c) =>
        UnityEngine.Random.value < .5f ? Faith(+5f, c, "legacy faith +5") : RandomFollowerFood(+15f);

    private string ApplyLegacyMedium(DonationEffectCommand c) =>
        UnityEngine.Random.value < .5f ? Faith(+10f, c, "legacy faith +10") : AllFollowerFood(+10f);

    private string ApplyLegacyHelpOrHinder(DonationEffectCommand c) =>
        UnityEngine.Random.value < .5f ? Faith(+15f, c, "legacy HELP faith +15") : Faith(-10f, c, "legacy HINDER faith -10");

    private string ApplyLegacySpecial(DonationEffectCommand c)
    {
        var feed = FullFeast();
        ChangeCultFaith(+20f, $"CHZZK SPECIAL by {c.Nickname}");
        return $"{feed}; faith +20";
    }

    private static void ChangeCultFaith(float amount, string reason)
    {
        var method = typeof(CultFaithManager)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "GetFaith")
            .FirstOrDefault(m => m.GetParameters().Length == 7);

        if (method == null)
            throw new MissingMethodException(typeof(CultFaithManager).FullName, "GetFaith");

        var p = method.GetParameters();
        var flairValue = p[3].ParameterType.IsEnum
            ? Enum.ToObject(p[3].ParameterType, 1)
            : Activator.CreateInstance(p[3].ParameterType);

        var args = new object?[] { 0f, amount, true, flairValue, reason, -1, Array.Empty<string>() };
        method.Invoke(null, args);
    }

    private static bool TryGetRandomFollower(out FollowerInfo follower)
    {
        var followers = DataManager.Instance.Followers;
        if (followers == null || followers.Count == 0)
        {
            follower = null!;
            return false;
        }

        var candidates = followers.Where(x => x != null).ToList();
        if (candidates.Count == 0)
        {
            follower = null!;
            return false;
        }

        follower = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        return true;
    }

    private static string Short(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value!.Substring(0, Math.Min(8, value.Length));

    private static double ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
}
