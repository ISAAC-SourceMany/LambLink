using System.Globalization;
using System.Text.Json.Serialization;

namespace LambLink.Companion.Chzzk;

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
    [property: JsonPropertyName("payAmount")] string PayAmount,
    [property: JsonPropertyName("donationText")] string? DonationText)
{
    [JsonIgnore] public string ReceptionId { get; init; } = string.Empty;
    [JsonIgnore] public DateTimeOffset ReceivedAtUtc { get; init; }

    public bool TryGetAmount(out long amount, out string error)
    {
        var value = PayAmount?.Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out amount))
        {
            amount = 0;
            error = "payAmount is not an invariant positive integer string";
            return false;
        }
        if (amount <= 0)
        {
            error = "payAmount must be positive";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed record SubscriptionEvent(
    [property: JsonPropertyName("channelId")] string ChannelId,
    [property: JsonPropertyName("subscriberChannelId")] string SubscriberChannelId,
    [property: JsonPropertyName("subscriberNickname")] string SubscriberNickname,
    [property: JsonPropertyName("tierNo")] int TierNo,
    [property: JsonPropertyName("tierName")] string TierName,
    [property: JsonPropertyName("month")] int Month);
