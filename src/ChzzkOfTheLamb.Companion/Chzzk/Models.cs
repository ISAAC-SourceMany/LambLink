using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChzzkOfTheLamb.Companion.Chzzk;

public sealed record ChzzkTokenSet(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    long ExpiresIn,
    DateTimeOffset AcquiredAt);

public sealed record ChzzkApiEnvelope<T>(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("content")] T? Content);

public sealed record SessionUrlContent(
    [property: JsonPropertyName("url")] string Url);

public sealed record MeContent(
    [property: JsonPropertyName("channelId")] string ChannelId,
    [property: JsonPropertyName("channelName")] string ChannelName);

public sealed record ChatProfile(
    [property: JsonPropertyName("nickname")] string Nickname);

public sealed record ChatEvent(
    [property: JsonPropertyName("channelId")] string ChannelId,
    [property: JsonPropertyName("senderChannelId")] string SenderChannelId,
    [property: JsonPropertyName("profile")] ChatProfile? Profile,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("messageTime")] long MessageTime);

public sealed record DonationEvent(
    [property: JsonPropertyName("donationType")] string DonationType,
    [property: JsonPropertyName("channelId")] string ChannelId,
    [property: JsonPropertyName("donatorChannelId")] string DonatorChannelId,
    [property: JsonPropertyName("donatorNickname")] string DonatorNickname,
    [property: JsonPropertyName("payAmount")] object PayAmount,
    [property: JsonPropertyName("donationText")] string? DonationText)
{
    public long ParsedAmount => PayAmount switch
    {
        JsonElement e when e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) => n,
        JsonElement e when e.ValueKind == JsonValueKind.String && long.TryParse(e.GetString(), out var n) => n,
        long n => n,
        int n => n,
        string s when long.TryParse(s, out var n) => n,
        _ => 0
    };
}

public sealed record SubscriptionEvent(
    [property: JsonPropertyName("channelId")] string ChannelId,
    [property: JsonPropertyName("subscriberChannelId")] string SubscriberChannelId,
    [property: JsonPropertyName("subscriberNickname")] string SubscriberNickname,
    [property: JsonPropertyName("tierNo")] int TierNo,
    [property: JsonPropertyName("tierName")] string TierName,
    [property: JsonPropertyName("month")] int Month);
