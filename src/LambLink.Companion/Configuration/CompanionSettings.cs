using System.Text.Json;

namespace LambLink.Companion.Configuration;

public sealed class CompanionSettings
{
    public RaffleSettings Raffle { get; set; } = new();
    public DonationSettings Donation { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public DevelopmentSettings Development { get; set; } = new();
    public CloudSettings Cloud { get; set; } = new();

    public static CompanionSettings LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var created = new CompanionSettings();
            File.WriteAllText(path, JsonSerializer.Serialize(created, JsonOptions));
            return created;
        }

        return JsonSerializer.Deserialize<CompanionSettings>(File.ReadAllText(path), JsonOptions)
               ?? new CompanionSettings();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

public sealed class RaffleSettings
{
    public string JoinCommand { get; set; } = "!신도";
    public string DeveloperSpawnCommand { get; set; } = "!신도테스트";
    public int DurationSeconds { get; set; } = 30;
    public bool AutoStartOnGameRequest { get; set; } = true;
    public bool PreventDuplicateEntry { get; set; } = true;
    public bool PreventExistingFollowerInSameSave { get; set; } = true;
}

public sealed class DonationSettings
{
    public bool Enabled { get; set; } = true;
    public List<DonationTierSettings> Tiers { get; set; } = new()
    {
        new()
        {
            MinAmount = 1_000, MaxAmount = 2_999,
            EventPool =
            [
                "SMALL_FAITH_UP_5",
                "SMALL_FAITH_DOWN_5",
                "SMALL_RANDOM_FOLLOWER_FOOD_UP_15",
                "SMALL_ALL_FOOD_UP_5",
                "SMALL_BALANCED_UP_3"
            ]
        },
        new()
        {
            MinAmount = 3_000, MaxAmount = 4_999,
            EventPool =
            [
                "MEDIUM_FAITH_UP_10",
                "MEDIUM_FAITH_DOWN_10",
                "MEDIUM_ALL_FOOD_UP_10",
                "MEDIUM_ALL_FOOD_DOWN_10",
                "MEDIUM_BALANCED_UP_7",
                "MEDIUM_BALANCED_DOWN_7"
            ]
        },
        new()
        {
            MinAmount = 5_000, MaxAmount = 9_999,
            EventPool =
            [
                "HH_FAITH_UP_20",
                "HH_ALL_FOOD_UP_20",
                "HH_BALANCED_UP_15",
                "HH_FAITH_DOWN_15",
                "HH_ALL_FOOD_DOWN_15",
                "HH_BALANCED_DOWN_10"
            ]
        },
        new()
        {
            MinAmount = 10_000, MaxAmount = null,
            EventPool =
            [
                "SPECIAL_FULL_FEAST",
                "SPECIAL_FAITH_UP_30",
                "SPECIAL_BALANCED_UP_25",
                "SPECIAL_FAITH_DOWN_20",
                "SPECIAL_BALANCED_DOWN_20",
                "SPECIAL_RANDOM_FOLLOWER_FEAST_FAITH_20"
            ]
        }
    };
}

public sealed class DonationTierSettings
{
    public long MinAmount { get; set; }
    public long? MaxAmount { get; set; }
    public List<string> EventPool { get; set; } = new();
}

public sealed class AppearanceSettings
{
    public bool IncludeModdedForms { get; set; }
    public bool IncludeSpecialForms { get; set; }
    public bool AutoRefreshCatalog { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 30;
}

public sealed class DevelopmentSettings
{
    public string LocalStreamerId { get; set; } = "dev-local-streamer";
    public string LocalStreamerName { get; set; } = "Local Development";
}

public sealed class CloudSettings
{
    public bool Enabled { get; set; }
    public string ApiBaseUrl { get; set; } = string.Empty;
    public bool UploadCatalog { get; set; } = true;
    public bool PreferRemoteViewerAppearance { get; set; } = true;
    public string FrontendUrl { get; set; } = string.Empty;
}
