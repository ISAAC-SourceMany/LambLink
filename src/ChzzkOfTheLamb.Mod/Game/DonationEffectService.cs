using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ChzzkOfTheLamb.Protocol;
using Newtonsoft.Json;
using UnityEngine;

namespace ChzzkOfTheLamb.Mod.Game;

public sealed class DonationEffectService(ManualLogSource log)
{
    public DonationEffectResult ApplyFromJson(string payloadJson)
    {
        DonationEffectCommand? command = null;
        try
        {
            command = JsonConvert.DeserializeObject<DonationEffectCommand>(payloadJson)
                      ?? throw new InvalidOperationException("Invalid DonationEffect command.");

            log.LogInfo($"[DONATION][INPUT] request={Short(command.RequestId)}, viewer={command.ViewerId}, nickname='{command.Nickname}', amount={command.Amount}, effect={command.Effect}, event='{command.EventName}'");

            if (PlayerFarming.Instance == null || DataManager.Instance == null)
                throw new InvalidOperationException("game/base is not ready");

            var details = ApplyExact(command);
            log.LogInfo($"[DONATION][APPLIED] request={Short(command.RequestId)}, event='{command.EventName}', details={details}");

            return new DonationEffectResult
            {
                RequestId = command.RequestId,
                Success = true,
                Effect = command.Effect,
                EventName = command.EventName,
                Amount = command.Amount,
                Nickname = command.Nickname,
                Details = details
            };
        }
        catch (Exception ex)
        {
            var root = ex.GetBaseException();
            log.LogWarning($"[DONATION][FAILED] request={Short(command?.RequestId)}, effect={command?.Effect ?? "unknown"}: {root.Message}");
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
            // 1,000 ~ 2,999
            "SMALL_FAITH_UP_5" => Faith(+5f, command, "faith +5"),
            "SMALL_FAITH_DOWN_5" => Faith(-5f, command, "faith -5"),
            "SMALL_RANDOM_FOLLOWER_FOOD_UP_15" => RandomFollowerFood(+15f),
            "SMALL_ALL_FOOD_UP_5" => AllFollowerFood(+5f),
            "SMALL_BALANCED_UP_3" => FaithAndFood(+3f, +3f, command),

            // 3,000 ~ 4,999
            "MEDIUM_FAITH_UP_10" => Faith(+10f, command, "faith +10"),
            "MEDIUM_FAITH_DOWN_10" => Faith(-10f, command, "faith -10"),
            "MEDIUM_ALL_FOOD_UP_10" => AllFollowerFood(+10f),
            "MEDIUM_ALL_FOOD_DOWN_10" => AllFollowerFood(-10f),
            "MEDIUM_BALANCED_UP_7" => FaithAndFood(+7f, +7f, command),
            "MEDIUM_BALANCED_DOWN_7" => FaithAndFood(-7f, -7f, command),

            // 5,000 ~ 9,999
            "HH_FAITH_UP_20" => Faith(+20f, command, "faith +20"),
            "HH_ALL_FOOD_UP_20" => AllFollowerFood(+20f),
            "HH_BALANCED_UP_15" => FaithAndFood(+15f, +15f, command),
            "HH_FAITH_DOWN_15" => Faith(-15f, command, "faith -15"),
            "HH_ALL_FOOD_DOWN_15" => AllFollowerFood(-15f),
            "HH_BALANCED_DOWN_10" => FaithAndFood(-10f, -10f, command),

            // 10,000+
            "SPECIAL_FULL_FEAST" => FullFeast(),
            "SPECIAL_FAITH_UP_30" => Faith(+30f, command, "faith +30"),
            "SPECIAL_BALANCED_UP_25" => FaithAndFood(+25f, +25f, command),
            "SPECIAL_FAITH_DOWN_20" => Faith(-20f, command, "faith -20"),
            "SPECIAL_BALANCED_DOWN_20" => FaithAndFood(-20f, -20f, command),
            "SPECIAL_RANDOM_FOLLOWER_FEAST_FAITH_20" => ChosenFollowerBlessing(command),

            // Backward-compatible tier keys in case a custom/old settings.json explicitly emits one.
            "SMALL_RANDOM" => ApplyLegacySmall(command),
            "MEDIUM_RANDOM" => ApplyLegacyMedium(command),
            "HELP_OR_HINDER_RANDOM" => ApplyLegacyHelpOrHinder(command),
            "SPECIAL_RANDOM" => ApplyLegacySpecial(command),

            _ => throw new InvalidOperationException($"Unknown donation effect: {command.Effect}")
        };
    }

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
        return $"{follower.Name} satiation {(amount >= 0 ? "+" : string.Empty)}{amount:0}";
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
        return $"faith +20; {follower.Name} fully fed";
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

        var args = new object?[]
        {
            0f,
            amount,
            true,
            flairValue,
            reason,
            -1,
            Array.Empty<string>()
        };

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
}
