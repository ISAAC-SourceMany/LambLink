using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ChzzkOfTheLamb.Companion.Diagnostics;

internal static class DiagnosticPrivacy
{
    private static readonly Regex ChannelId = new(@"(?<![0-9a-fA-F])[0-9a-fA-F]{32}(?![0-9a-fA-F])", RegexOptions.Compiled);
    private static readonly Regex UserPath = new(@"(?i)([A-Z]:\\Users\\)[^\\\s]+", RegexOptions.Compiled);
    private static readonly Regex UnixUserPath = new(@"/(?:home|Users)/[^/\s]+", RegexOptions.Compiled);
    private static readonly Regex QuotedMessage = new("(?i)\\b(content|text|message)=('[^']*'|\"[^\"]*\")", RegexOptions.Compiled);
    private static readonly Regex QuotedNickname = new("(?i)\\b(nickname|subscriberNickname)=('[^']*'|\"[^\"]*\")", RegexOptions.Compiled);
    private static readonly Regex BareIdentity = new(@"(?i)\b(viewer|sender|channel|viewerId|streamer)=([^,;\s\]\)]+)", RegexOptions.Compiled);
    private static readonly Regex StatusChzzkName = new(@"(?i)\bCHZZK=[^,\r\n]+", RegexOptions.Compiled);
    private static readonly Regex BareNickname = new(@"(?i)\bnickname=.+?(?=\s+content=|,|;|$)", RegexOptions.Compiled);
    private static readonly Regex RaffleDisplayName = new(@"(?i)(\b(?:joined|winner|rejected|created|identity applied):\s+).+?(?=\s+\(|\s+->|,\s*(?:ID|followerId|participants)=|$)", RegexOptions.Compiled);
    private static readonly Regex JsonSensitiveValue = new("(?i)\"(donationText|content|message|nickname|donatorNickname|subscriberNickname)\"\\s*:\\s*\"(?:\\\\.|[^\"])*\"", RegexOptions.Compiled);
    private static readonly Regex JsonIdentityValue = new("(?i)\"(channelId|senderChannelId|donatorChannelId|subscriberChannelId|viewerId|streamerChannelId)\"\\s*:\\s*\"(?:\\\\.|[^\"])*\"", RegexOptions.Compiled);
    private static readonly Regex Secret = new(@"(?i)\b(access[_ -]?token|refresh[_ -]?token|authorization|bearer|client[_ -]?secret)\b\s*[:=]?\s*[^,;\s]+", RegexOptions.Compiled);
    private static readonly Regex ConnectedName = new(@"(?i)(\[(?:AUTH|CHZZK)\][^\r\n]*connected:\s+)[^(]+(?=\s+\([0-9a-fA-F]{32}\))", RegexOptions.Compiled);

    public static string ShortHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "none";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    public static string StableFileKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "none";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    public static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var redacted = ConnectedName.Replace(input, "$1<streamer>");
        redacted = ChannelId.Replace(redacted, match => $"<id:{ShortHash(match.Value)}>");
        redacted = UserPath.Replace(redacted, "$1<user>");
        redacted = UnixUserPath.Replace(redacted, "/home/<user>");
        redacted = QuotedMessage.Replace(redacted, "$1=<message-redacted>");
        redacted = QuotedNickname.Replace(redacted, "$1=<nickname-redacted>");
        redacted = BareIdentity.Replace(redacted, "$1=<identity-redacted>");
        redacted = StatusChzzkName.Replace(redacted, "CHZZK=<streamer-redacted>");
        redacted = BareNickname.Replace(redacted, "nickname=<nickname-redacted>");
        redacted = RaffleDisplayName.Replace(redacted, "$1<name-redacted>");
        redacted = JsonSensitiveValue.Replace(redacted, match =>
        {
            var separator = match.Value.IndexOf(':');
            return match.Value[..(separator + 1)] + "\"<redacted>\"";
        });
        redacted = JsonIdentityValue.Replace(redacted, match =>
        {
            var separator = match.Value.IndexOf(':');
            return match.Value[..(separator + 1)] + "\"<identity-redacted>\"";
        });
        redacted = Secret.Replace(redacted, "$1=<secret-redacted>");
        return redacted;
    }
}
