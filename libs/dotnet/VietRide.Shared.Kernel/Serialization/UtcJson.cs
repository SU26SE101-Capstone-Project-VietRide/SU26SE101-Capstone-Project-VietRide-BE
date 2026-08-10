using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VietRide.Shared.Kernel.Serialization;

/// <summary>
/// Canonical JSON policy for persistence, Redis, internal HTTP and integration events.
/// Every instant is normalized to UTC and serialized with a literal <c>Z</c> suffix.
/// </summary>
public static partial class UtcJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static JsonSerializerOptions IgnoreNullOptions { get; } = CreateOptions(
        JsonIgnoreCondition.WhenWritingNull);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string Serialize(object value, Type inputType) =>
        JsonSerializer.Serialize(value, inputType, Options);

    public static string NormalizeInstants(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = JsonNode.Parse(json)
            ?? throw new JsonException("JSON payload must not be null.");
        NormalizeNode(root, propertyName: null);
        return root.ToJsonString(Options);
    }

    public static byte[] NormalizeInstants(ReadOnlySpan<byte> json)
    {
        var root = JsonNode.Parse(json)
            ?? throw new JsonException("JSON payload must not be null.");
        NormalizeNode(root, propertyName: null);
        return JsonSerializer.SerializeToUtf8Bytes(root, Options);
    }

    public static bool TryParseInstant(string? raw, out DateTimeOffset instant)
    {
        instant = default;
        return raw is not null
            && InstantPattern().IsMatch(raw)
            && DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out instant);
    }

    public static bool IsInstantPropertyName(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return false;

        return !propertyName.Equals("message", StringComparison.OrdinalIgnoreCase)
            && !propertyName.Equals("description", StringComparison.OrdinalIgnoreCase)
            && !propertyName.Equals("title", StringComparison.OrdinalIgnoreCase)
            && !propertyName.Equals("body", StringComparison.OrdinalIgnoreCase)
            && !propertyName.Equals("content", StringComparison.OrdinalIgnoreCase)
            && !propertyName.Equals("note", StringComparison.OrdinalIgnoreCase)
            && !propertyName.Equals("reason", StringComparison.OrdinalIgnoreCase)
            && !propertyName.Equals("text", StringComparison.OrdinalIgnoreCase);
    }

    public static string Format(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static JsonSerializerOptions CreateOptions(
        JsonIgnoreCondition ignoreCondition = JsonIgnoreCondition.Never)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = ignoreCondition,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        options.Converters.Add(new UtcDateTimeConverter());
        return options;
    }

    private static void NormalizeNode(JsonNode node, string? propertyName)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Value is JsonValue value
                    && IsInstantPropertyName(property.Key)
                    && value.TryGetValue<string>(out var raw)
                    && TryParseInstant(raw, out var parsed))
                {
                    obj[property.Key] = Format(parsed);
                }
                else if (property.Value is not null)
                {
                    NormalizeNode(property.Value, property.Key);
                }
            }

            return;
        }

        if (node is not JsonArray array)
            return;

        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is JsonValue value
                && IsInstantPropertyName(propertyName)
                && value.TryGetValue<string>(out var raw)
                && TryParseInstant(raw, out var parsed))
            {
                array[index] = Format(parsed);
            }
            else if (array[index] is not null)
            {
                NormalizeNode(array[index]!, propertyName);
            }
        }
    }

    [GeneratedRegex(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex InstantPattern();

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (!TryParseInstant(raw, out var parsed))
            {
                throw new JsonException("Timestamp must be RFC 3339 with Z or an explicit offset.");
            }

            return parsed.ToUniversalTime();
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.UtcDateTime);
    }

    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (!TryParseInstant(raw, out var parsed))
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
            writer.WriteStringValue(utc);
        }
    }
}
