using LambLink.Companion.Configuration;

namespace LambLink.Companion.Rules;

public sealed record DonationDecision(string Effect, string EventName, string TierName);

public sealed class DonationRuleEngine(DonationSettings settings)
{
    private static readonly IReadOnlyDictionary<string, (string Name, string Tier)> Catalog =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["SMALL_FAITH_UP_5"] = ("작은 신앙의 축복", "SMALL"),
            ["SMALL_FAITH_DOWN_5"] = ("작은 신앙의 흔들림", "SMALL"),
            ["SMALL_RANDOM_FOLLOWER_FOOD_UP_15"] = ("간식 배달", "SMALL"),
            ["SMALL_ALL_FOOD_UP_5"] = ("소박한 식사", "SMALL"),
            ["SMALL_BALANCED_UP_3"] = ("작은 격려", "SMALL"),

            ["MEDIUM_FAITH_UP_10"] = ("신앙의 응원", "MEDIUM"),
            ["MEDIUM_FAITH_DOWN_10"] = ("불안의 속삭임", "MEDIUM"),
            ["MEDIUM_ALL_FOOD_UP_10"] = ("풍족한 한 끼", "MEDIUM"),
            ["MEDIUM_ALL_FOOD_DOWN_10"] = ("식량난", "MEDIUM"),
            ["MEDIUM_BALANCED_UP_7"] = ("교단 활력", "MEDIUM"),
            ["MEDIUM_BALANCED_DOWN_7"] = ("교단 침체", "MEDIUM"),

            ["HH_FAITH_UP_20"] = ("강한 신앙의 축복", "HELP_OR_HINDER"),
            ["HH_ALL_FOOD_UP_20"] = ("대규모 만찬", "HELP_OR_HINDER"),
            ["HH_BALANCED_UP_15"] = ("풍요의 날", "HELP_OR_HINDER"),
            ["HH_FAITH_DOWN_15"] = ("신앙의 위기", "HELP_OR_HINDER"),
            ["HH_ALL_FOOD_DOWN_15"] = ("대규모 공복", "HELP_OR_HINDER"),
            ["HH_BALANCED_DOWN_10"] = ("고난의 날", "HELP_OR_HINDER"),

            ["SPECIAL_FULL_FEAST"] = ("전원 포식의 축복", "SPECIAL"),
            ["SPECIAL_FAITH_UP_30"] = ("대신앙의 기적", "SPECIAL"),
            ["SPECIAL_BALANCED_UP_25"] = ("대축복", "SPECIAL"),
            ["SPECIAL_FAITH_DOWN_20"] = ("대혼란", "SPECIAL"),
            ["SPECIAL_BALANCED_DOWN_20"] = ("대재난", "SPECIAL"),
            ["SPECIAL_RANDOM_FOLLOWER_FEAST_FAITH_20"] = ("선택받은 신도의 축복", "SPECIAL"),

            ["DUNGEON_HEAL_SMALL"] = ("작은 치유", "DUNGEON_SMALL"),
            ["DUNGEON_HURT_SMALL"] = ("작은 시련", "DUNGEON_SMALL"),
            ["DUNGEON_FERVOUR_SMALL"] = ("열정 충전", "DUNGEON_SMALL"),
            ["DUNGEON_SPEED_SMALL"] = ("신속의 축복", "DUNGEON_SMALL"),
            ["DUNGEON_SPEED_DOWN_SMALL"] = ("둔화의 장난", "DUNGEON_SMALL"),
            ["DUNGEON_ENEMY_DAMAGE_SMALL"] = ("적을 향한 일격", "DUNGEON_SMALL"),

            ["DUNGEON_HEAL_MEDIUM"] = ("치유의 손길", "DUNGEON_MEDIUM"),
            ["DUNGEON_HURT_MEDIUM"] = ("고통의 장난", "DUNGEON_MEDIUM"),
            ["DUNGEON_FERVOUR_MEDIUM"] = ("열정의 샘", "DUNGEON_MEDIUM"),
            ["DUNGEON_SPEED_MEDIUM"] = ("질주의 축복", "DUNGEON_MEDIUM"),
            ["DUNGEON_ATTACK_MEDIUM"] = ("전투의 축복", "DUNGEON_MEDIUM"),
            ["DUNGEON_SPEED_DOWN_MEDIUM"] = ("무거운 발걸음", "DUNGEON_MEDIUM"),
            ["DUNGEON_ATTACK_DOWN_MEDIUM"] = ("무뎌진 칼날", "DUNGEON_MEDIUM"),
            ["DUNGEON_ENEMY_DAMAGE_MEDIUM"] = ("적 무리 강타", "DUNGEON_MEDIUM"),

            ["DUNGEON_HEAL_LARGE"] = ("강한 치유", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_HURT_LARGE"] = ("강한 시련", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_FERVOUR_LARGE"] = ("넘치는 열정", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_SPEED_ATTACK_LARGE"] = ("광전사의 축복", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_SPEED_ATTACK_DOWN_LARGE"] = ("쇠약의 저주", "DUNGEON_HELP_OR_HINDER"),
            ["DUNGEON_ENEMY_DAMAGE_LARGE"] = ("적 무리 대타격", "DUNGEON_HELP_OR_HINDER"),

            ["DUNGEON_HEAL_SPECIAL"] = ("기적의 치유", "DUNGEON_SPECIAL"),
            ["DUNGEON_HURT_SPECIAL"] = ("신의 시련", "DUNGEON_SPECIAL"),
            ["DUNGEON_FERVOUR_SPECIAL"] = ("열정 완전 충전", "DUNGEON_SPECIAL"),
            ["DUNGEON_SPEED_ATTACK_SPECIAL"] = ("전투의 기적", "DUNGEON_SPECIAL"),
            ["DUNGEON_SPEED_ATTACK_DOWN_SPECIAL"] = ("전투의 대저주", "DUNGEON_SPECIAL"),
            ["DUNGEON_ENEMY_DAMAGE_SPECIAL"] = ("적 무리 대폭발", "DUNGEON_SPECIAL"),
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
