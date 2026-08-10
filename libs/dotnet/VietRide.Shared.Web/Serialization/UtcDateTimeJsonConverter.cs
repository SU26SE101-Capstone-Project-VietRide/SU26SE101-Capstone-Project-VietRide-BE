using System.Text.Json;
using System.Text.Json.Serialization;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Shared.Web.Serialization;

/// <summary>
/// Compatibility converter for legacy API DTO instants that still use DateTime.
/// New instant DTOs should use DateTimeOffset.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    private readonly Func<bool> useVietnamPresentation;

    public UtcDateTimeJsonConverter(Func<bool>? useVietnamPresentation = null)
    {
        this.useVietnamPresentation = useVietnamPresentation ?? (() => false);
    }

    public override DateTime Read(
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

        return parsed.UtcDateTime;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        if (useVietnamPresentation())
        {
            var instant = new DateTimeOffset(utc, TimeSpan.Zero);
            writer.WriteStringValue(ApiTimestampPresentation.ForResponse(instant, frontend: true));
            return;
        }

        writer.WriteStringValue(utc);
    }
}
