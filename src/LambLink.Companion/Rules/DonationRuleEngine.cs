using LambLink.Companion.Configuration;

namespace LambLink.Companion.Rules;

public sealed record DonationDecision(string Effect, string EventName, string TierName);

public sealed class DonationRuleEngine(DonationSettings settings)
{
    private static readonly IReadOnlyDictionary<string, (string Name, string Tier)> Catalog =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["SMALL_FAITH_UP_5"] = ("신앙 5 증가", "SMALL"),
            ["SMALL_FAITH_DOWN_5"] = ("신앙 5 감소", "SMALL"),
            ["SMALL_RANDOM_FOLLOWER_FOOD_UP_15"] = ("무작위 신도 1명 포만도 15 증가", "SMALL"),
            ["SMALL_ALL_FOOD_UP_5"] = ("모든 신도 포만도 5 증가", "SMALL"),
            ["SMALL_BALANCED_UP_3"] = ("신앙·모든 신도 포만도 3 증가", "SMALL"),

            ["MEDIUM_FAITH_UP_10"] = ("신앙 10 증가", "MEDIUM"),
            ["MEDIUM_FAITH_DOWN_10"] = ("신앙 10 감소", "MEDIUM"),
            ["MEDIUM_ALL_FOOD_UP_10"] = ("모든 신도 포만도 10 증가", "MEDIUM"),
            ["MEDIUM_ALL_FOOD_DOWN_10"] = ("모든 신도 포만도 10 감소", "MEDIUM"),
            ["MEDIUM_BALANCED_UP_7"] = ("신앙·모든 신도 포만도 7 증가", "MEDIUM"),
            ["MEDIUM_BALANCED_DOWN_7"] = ("신앙·모든 신도 포만도 7 감소", "MEDIUM"),

            ["HH_FAITH_UP_20"] = ("신앙 20 증가", "HELP_OR_HINDER"),
            ["HH_ALL_FOOD_UP_20"] = ("모든 신도 포만도 20 증가", "HELP_OR_HINDER"),
            ["HH_BALANCED_UP_15"] = ("신앙·모든 신도 포만도 15 증가", "HELP_OR_HINDER"),
            ["HH_FAITH_DOWN_15"] = ("신앙 15 감소", "HELP_OR_HINDER"),
            ["HH_ALL_FOOD_DOWN_15"] = ("모든 신도 포만도 15 감소", "HELP_OR_HINDER"),
            ["HH_BALANCED_DOWN_10"] = ("신앙·모든 신도 포만도 10 감소", "HELP_OR_HINDER"),

            ["SPECIAL_FULL_FEAST"] = ("모든 신도 포만도 완전 회복", "SPECIAL"),
            ["SPECIAL_FAITH_UP_30"] = ("신앙 30 증가", "SPECIAL"),
            ["SPECIAL_BALANCED_UP_25"] = ("신앙·모든 신도 포만도 25 증가", "SPECIAL"),
            ["SPECIAL_FAITH_DOWN_20"] = ("신앙 20 감소", "SPECIAL"),
            ["SPECIAL_BALANCED_DOWN_20"] = ("신앙·모든 신도 포만도 20 감소", "SPECIAL"),
            ["SPECIAL_RANDOM_FOLLOWER_FEAST_FAITH_20"] = ("신앙 20 증가·무작위 신도 1명 포만도 완전 회복", "SPECIAL"),

            ["DUNGEON_HEAL_SMALL"] = ("체력 하트 0.5칸 회복", "DUNGEON_SMALL"),
            ["DUNGEON_HURT_SMALL"] = ("체력 하트 0.5칸 감소", "DUNGEON_SMALL"),
            ["DUNGEON_FERVOUR_SMALL"] = ("열정 최대치의 20% 회복", "DUNGEON_SMALL"),
            ["DUNGEON_SPEED_SMALL"] = ("이동속도 15% 증가", "DUNGEON_SMALL"),
            ["DUNGEON_SPEED_DOWN_SMALL"] = ("이동속도 15% 감소", "DUNGEON_SMALL"),
            ["DUNGEON_ENEMY_DAMAGE_SMALL"] = ("적 전체에 고정 피해 0.5", "DUNGEON_SMALL"),

            ["DUNGEON_HEAL_MEDIUM"] = ("체력 하트 1칸 회복", "DUNGEON_MEDIUM"),
            ["DUNGEON_HURT_MEDIUM"] = ("체력 하트 1칸 감소", "DUNGEON_MEDIUM"),
            ["DUNGEON_FERVOUR_MEDIUM"] = ("열정 최대치의 35% 회복", "DUNGEON_MEDIUM"),
            ["DUNGEON_SPEED_MEDIUM"] = ("이동속도 20% 증가", "DUNGEON_MEDIUM"),
            ["DUNGEON_ATTACK_MEDIUM"] = ("공격력 20% 증가", "DUNGEON_MEDIUM"),
            ["DUNGEON_SPEED_DOWN_MEDIUM"] = ("이동속도 20% 감소", "DUNGEON_MEDIUM"),
            ["DUNGEON_ATTACK_DOWN_MEDIUM"] = ("공격력 20% 감소", "DUNGEON_MEDIUM"),
            ["DUNGEON_ENEMY_DAMAGE_MEDIUM"] = ("적 전체에 고정 피해 1", "DUNGEON_MEDIUM"),

            ["DUNGEON_HEAL_LARGE"] = ("체력 하트 1.5칸 회복", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_HURT_LARGE"] = ("체력 하트 1.5칸 감소", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_FERVOUR_LARGE"] = ("열정 최대치의 50% 회복", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_SPEED_ATTACK_LARGE"] = ("이동속도·공격력 25% 증가", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_SPEED_ATTACK_DOWN_LARGE"] = ("이동속도·공격력 25% 감소", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_ENEMY_DAMAGE_LARGE"] = ("적 전체에 고정 피해 1.5", "DUNGEON_HELP_OR_HINDER"),

            ["DUNGEON_HEAL_SPECIAL"] = ("체력 하트 2칸 회복", "DUNGEON_SPECIAL"),
            ["DUNGEON_HURT_SPECIAL"] = ("체력 하트 2칸 감소", "DUNGEON_SPECIAL"),
            ["DUNGEON_FERVOUR_SPECIAL"] = ("열정 완전 회복", "DUNGEON_SPECIAL"),
            ["DUNGEON_SPEED_ATTACK_SPECIAL"] = ("이동속도·공격력 40% 증가", "DUNGEON_SPECIAL"),
            ["DUNGEON_SPEED_ATTACK_DOWN_SPECIAL"] = ("이동속도·공격력 40% 감소", "DUNGEON_SPECIAL"),
            ["DUNGEON_ENEMY_DAMAGE_SPECIAL"] = ("적 전체에 고정 피해 2.5", "DUNGEON_SPECIAL"),
        };

    public DonationDecision ResolveDecision(long amount, string? area = null)
    {
        if (!settings.Enabled) return new DonationDecision("NONE", "후원 이벤트 비활성화", "NONE");

        if (string.Equals(area, "DUNGEON", StringComparison.OrdinalIgnoreCase))
            return ResolveDungeon(amount);

        var tier = settings.Tiers.FirstOrDefault(x =>
            amount >= x.MinAmount && (x.MaxAmount is null || amount <= x.MaxAmount.Value));

        if (tier is null || tier.EventPool.Count == 0)
            return new DonationDecision("NONE", "해당 금액 이벤트 없음", "NONE");

        var effect = tier.EventPool[Random.Shared.Next(tier.EventPool.Count)];
        if (Catalog.TryGetValue(effect, out var info))
            return new DonationDecision(effect, info.Name, info.Tier);

        return effect switch
        {
            "SMALL_RANDOM" => ResolveFromDefaultPool(DefaultSmall, "SMALL"),
            "MEDIUM_RANDOM" => ResolveFromDefaultPool(DefaultMedium, "MEDIUM"),
            "HELP_OR_HINDER_RANDOM" => ResolveFromDefaultPool(DefaultHelpOrHinder, "HELP_OR_HINDER"),
            "SPECIAL_RANDOM" => ResolveFromDefaultPool(DefaultSpecial, "SPECIAL"),
            _ => new DonationDecision(effect, effect, "CUSTOM")
        };
    }

    public string Resolve(long amount) => ResolveDecision(amount).Effect;

    public string GetEventName(string effect) =>
        Catalog.TryGetValue(effect, out var info) ? info.Name : effect;

    private static DonationDecision ResolveDungeon(long amount)
    {
        if (amount < 1_000)
            return new DonationDecision("NONE", "해당 금액 이벤트 없음", "NONE");

        var pool = amount switch
        {
            <= 2_999 => DungeonSmall,
            <= 4_999 => DungeonMedium,
            <= 9_999 => DungeonLarge,
            _ => DungeonSpecial
        };
        var effect = pool[Random.Shared.Next(pool.Length)];
        var info = Catalog[effect];
        return new DonationDecision(effect, info.Name, info.Tier);
    }

    private static DonationDecision ResolveFromDefaultPool(string[] pool, string tier)
    {
        var effect = pool[Random.Shared.Next(pool.Length)];
        var info = Catalog[effect];
        return new DonationDecision(effect, info.Name, tier);
    }

    private static readonly string[] DefaultSmall =
    [
        "SMALL_FAITH_UP_5", "SMALL_FAITH_DOWN_5", "SMALL_RANDOM_FOLLOWER_FOOD_UP_15",
        "SMALL_ALL_FOOD_UP_5", "SMALL_BALANCED_UP_3"
    ];

    private static readonly string[] DefaultMedium =
    [
        "MEDIUM_FAITH_UP_10", "MEDIUM_FAITH_DOWN_10", "MEDIUM_ALL_FOOD_UP_10",
        "MEDIUM_ALL_FOOD_DOWN_10", "MEDIUM_BALANCED_UP_7", "MEDIUM_BALANCED_DOWN_7"
    ];

    private static readonly string[] DefaultHelpOrHinder =
    [
        "HH_FAITH_UP_20", "HH_ALL_FOOD_UP_20", "HH_BALANCED_UP_15",
        "HH_FAITH_DOWN_15", "HH_ALL_FOOD_DOWN_15", "HH_BALANCED_DOWN_10"
    ];

    private static readonly string[] DefaultSpecial =
    [
        "SPECIAL_FULL_FEAST", "SPECIAL_FAITH_UP_30", "SPECIAL_BALANCED_UP_25",
        "SPECIAL_FAITH_DOWN_20", "SPECIAL_BALANCED_DOWN_20", "SPECIAL_RANDOM_FOLLOWER_FEAST_FAITH_20"
    ];

    private static readonly string[] DungeonSmall =
    [
        "DUNGEON_HEAL_SMALL", "DUNGEON_HURT_SMALL", "DUNGEON_FERVOUR_SMALL",
        "DUNGEON_SPEED_SMALL", "DUNGEON_SPEED_DOWN_SMALL", "DUNGEON_ENEMY_DAMAGE_SMALL"
    ];

    private static readonly string[] DungeonMedium =
    [
        "DUNGEON_HEAL_MEDIUM", "DUNGEON_HURT_MEDIUM", "DUNGEON_FERVOUR_MEDIUM",
        "DUNGEON_SPEED_MEDIUM", "DUNGEON_ATTACK_MEDIUM", "DUNGEON_SPEED_DOWN_MEDIUM",
        "DUNGEON_ATTACK_DOWN_MEDIUM", "DUNGEON_ENEMY_DAMAGE_MEDIUM"
    ];

    private static readonly string[] DungeonLarge =
    [
        "DUNGEON_HEAL_LARGE", "DUNGEON_HURT_LARGE", "DUNGEON_FERVOUR_LARGE",
        "DUNGEON_SPEED_ATTACK_LARGE", "DUNGEON_SPEED_ATTACK_DOWN_LARGE", "DUNGEON_ENEMY_DAMAGE_LARGE"
    ];

    private static readonly string[] DungeonSpecial =
    [
        "DUNGEON_HEAL_SPECIAL", "DUNGEON_HURT_SPECIAL", "DUNGEON_FERVOUR_SPECIAL",
        "DUNGEON_SPEED_ATTACK_SPECIAL", "DUNGEON_SPEED_ATTACK_DOWN_SPECIAL", "DUNGEON_ENEMY_DAMAGE_SPECIAL"
    ];
}
