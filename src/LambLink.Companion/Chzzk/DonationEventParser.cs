using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LambLink.Companion.Chzzk;

public sealed record DonationParseResult(DonationEvent? Donation, string Code, string Field, string ActualKind)
{
    public bool Success => Donation is not null;
}

/// <summary>Strict, donation-only normalization. Diagnostics never contain input values or arbitrary field names.</summary>
public static class DonationEventParser
{
    public const int MaximumPayloadBytes = 64 * 1024;
    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
        { "donationType", "channelId", "donatorChannelId", "donatorNickname", "payAmount", "donationText" };

    public static DonationParseResult Parse(string payload)
    {
        if (Encoding.UTF8.GetByteCount(payload) > MaximumPayloadBytes) return Fail("PAYLOAD_TOO_LARGE", "$", "Unknown");
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            // Socket.IO string arguments are normally decoded by the transport. Accept one
            // additional JSON-string argument here for standalone fixtures/callers, never recurse.
            if (root.ValueKind == JsonValueKind.String)
            {
                using var inner = JsonDocument.Parse(root.GetString()!, new JsonDocumentOptions { MaxDepth = 16 });
                return ParseObject(inner.RootElement);
            }
            return ParseObject(root);
        }
        catch (JsonException) { return Fail("INVALID_JSON", "$", "Unknown"); }
    }

    private static DonationParseResult ParseObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return Fail("INVALID_ROOT", "$", root.ValueKind.ToString());
        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind != JsonValueKind.Object) return Fail("INVALID_ENVELOPE", "data", data.ValueKind.ToString());
            if (root.EnumerateObject().Count(p => p.NameEquals("data")) != 1) return Fail("DUPLICATE_FIELD", "data", "Object");
            root = data;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (KnownFields.Contains(property.Name) && !seen.Add(property.Name))
                return Fail("DUPLICATE_FIELD", property.Name, property.Value.ValueKind.ToString());

        foreach (var field in KnownFields)
        {
            if (field == "payAmount") continue;
            var required = field is "channelId" or "donationType";
            if (!root.TryGetProperty(field, out var node))
            {
                if (required) return Fail("MISSING_FIELD", field, "Missing");
                continue;
            }
            if (node.ValueKind == JsonValueKind.Null && !required) continue;
            if (node.ValueKind != JsonValueKind.String) return Fail("INVALID_TYPE", field, node.ValueKind.ToString());
            if (required && string.IsNullOrWhiteSpace(node.GetString())) return Fail("EMPTY_FIELD", field, "String");
        }
        if (!root.TryGetProperty("payAmount", out var amountNode)) return Fail("MISSING_FIELD", "payAmount", "Missing");
        if (amountNode.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            return Fail("INVALID_TYPE", "payAmount", amountNode.ValueKind.ToString());
        var value = amountNode.ValueKind == JsonValueKind.String ? amountNode.GetString()!.Trim() : amountNode.GetRawText();
        if (value.Length == 0 || value.Any(c => c < '0' || c > '9'))
            return Fail("INVALID_INTEGER", "payAmount", amountNode.ValueKind.ToString());
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
            return Fail("AMOUNT_OVERFLOW", "payAmount", amountNode.ValueKind.ToString());
        if (amount <= 0) return Fail("NON_POSITIVE_AMOUNT", "payAmount", amountNode.ValueKind.ToString());
        string Read(string field) => root.TryGetProperty(field, out var node) && node.ValueKind == JsonValueKind.String ? node.GetString()! : string.Empty;
        var type = Read("donationType");
        if (type is not ("CHAT" or "VIDEO")) return Fail("UNSUPPORTED_DONATION_TYPE", "donationType", "String");
        return new(new DonationEvent(type, Read("channelId"), Read("donatorChannelId"), Read("donatorNickname"),
            amount.ToString(CultureInfo.InvariantCulture), Read("donationText")), "OK", "payAmount", amountNode.ValueKind.ToString());
    }

    private static DonationParseResult Fail(string code, string field, string kind) => new(null, code, field, kind);
}

public sealed record DonationReception(string Id, string SessionId, long Sequence, DateTimeOffset ReceivedAtUtc,
    int PayloadBytes, DonationParseResult Result);
