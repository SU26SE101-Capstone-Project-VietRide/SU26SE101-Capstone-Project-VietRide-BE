using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Serialization;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Shared.Web.Serialization;

public static partial class ApiTimestampPresentation
{
    public static bool IsFrontendRequest(HttpContext? context)
    {
        if (context is null)
            return false;

        var path = context.Request.Path;
        return path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase);
    }

    public static DateTimeOffset ForResponse(DateTimeOffset instant, bool frontend) =>
        frontend ? BusinessTime.ToLocalOffset(instant) : instant.ToUniversalTime();

    public static string FormatForResponse(DateTimeOffset instant, bool frontend)
    {
        var value = ForResponse(instant, frontend);
        return frontend
            ? value.ToString("O", CultureInfo.InvariantCulture)
            : value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    public static ApiMeta CreateMeta(HttpContext context, string traceId) =>
        new()
        {
            TraceId = traceId,
            Timestamp = ForResponse(DateTimeOffset.UtcNow, IsFrontendRequest(context)),
        };

    public static JsonSerializerOptions CreateSerializerOptions(HttpContext context)
    {
        var frontend = IsFrontendRequest(context);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new UtcDateTimeOffsetJsonConverter(() => frontend));
        options.Converters.Add(new UtcDateTimeJsonConverter(() => frontend));
        options.Converters.Add(new FrontendJsonElementConverter(() => frontend));
        return options;
    }

    public static byte[] TransformCachedJsonForResponse(
        byte[] body,
        string? contentType,
        HttpContext context)
    {
        if (!IsFrontendRequest(context)
            || body.Length == 0)
        {
            return body;
        }

        return TransformJsonForFrontend(body, contentType);
    }

    public static byte[] TransformJsonForFrontend(byte[] body, string? contentType)
    {
        if (body.Length == 0
            || contentType is null
            || !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return body;
        }

        if (root is null)
            return body;

        TransformNode(root, propertyName: null);
        return JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static void TransformNode(JsonNode node, string? propertyName)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Value is JsonValue value
                    && UtcJson.IsInstantPropertyName(property.Key)
                    && value.TryGetValue<string>(out var raw)
                    && TryConvertInstant(raw, out var converted))
                {
                    obj[property.Key] = converted;
                }
                else if (property.Value is not null)
                {
                    TransformNode(property.Value, property.Key);
                }
            }

            return;
        }

        if (node is not JsonArray array)
            return;

        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is JsonValue value
                && UtcJson.IsInstantPropertyName(propertyName)
                && value.TryGetValue<string>(out var raw)
                && TryConvertInstant(raw, out var converted))
            {
                array[index] = converted;
            }
            else if (array[index] is not null)
            {
                TransformNode(array[index]!, propertyName);
            }
        }
    }

    private static bool TryConvertInstant(string raw, out string converted)
    {
        converted = string.Empty;
        if (!UtcJson.TryParseInstant(raw, out var parsed))
        {
            return false;
        }

        converted = FormatForResponse(parsed, frontend: true);
        return true;
    }
}
