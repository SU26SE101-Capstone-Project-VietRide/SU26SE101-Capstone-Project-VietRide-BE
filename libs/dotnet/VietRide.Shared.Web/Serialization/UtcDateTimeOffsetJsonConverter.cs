using System.Text.Json;
using System.Text.Json.Serialization;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Shared.Web.Serialization;

/// <summary>
/// Requires an explicit offset on input, normalizes it to UTC, and uses the configured response
/// boundary to emit either Vietnam presentation time or canonical UTC.
/// </summary>
public sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private readonly Func<bool> useVietnamPresentation;

    public UtcDateTimeOffsetJsonConverter(Func<bool>? useVietnamPresentation = null)
    {
        this.useVietnamPresentation = useVietnamPresentation ?? (() => false);
    }

    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Timestamp must be an ISO-8601 string with Z or an explicit offset.");
        }

        var raw = reader.GetString();
        if (!UtcJson.TryParseInstant(raw, out var parsed))
        {
            throw new JsonException("Timestamp must be RFC 3339 with Z or an explicit offset.");
        }

        return parsed.ToUniversalTime();
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options) =>
        WriteResponseValue(writer, value);

    private void WriteResponseValue(Utf8JsonWriter writer, DateTimeOffset value)
    {
        if (useVietnamPresentation())
        {
            writer.WriteStringValue(ApiTimestampPresentation.ForResponse(value, frontend: true));
            return;
        }

        writer.WriteStringValue(value.UtcDateTime);
    }
}
