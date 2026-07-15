using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace VietRide.Shared.Web.Idempotency;

internal static class IdempotencyFingerprint
{
    private const string EmptyBodyMarker = "<empty>";

    public static async Task<string> ComputeAsync(HttpContext context)
    {
        var components = new FingerprintComponents(
            context.Request.Method.ToUpperInvariant(),
            NormalizeRoute(context),
            NormalizeRouteValues(context.Request.RouteValues),
            NormalizeQuery(context.Request.Query),
            ResolveSubject(context.User),
            await CanonicalizeBodyAsync(context.Request));

        var bytes = JsonSerializer.SerializeToUtf8Bytes(components);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static string ResolveSubject(ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return subject;
        }

        subject = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(subject) ? string.Empty : subject;
    }

    private static string NormalizeRoute(HttpContext context)
    {
        var template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        var route = string.IsNullOrWhiteSpace(template)
            ? context.Request.Path.Value ?? "/"
            : template;

        route = Uri.UnescapeDataString(route.Trim());
        if (!route.StartsWith('/'))
        {
            route = $"/{route}";
        }

        return route.TrimEnd('/').ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, string> NormalizeRouteValues(RouteValueDictionary routeValues)
    {
        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in routeValues)
        {
            var value = Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            normalized[pair.Key.ToLowerInvariant()] = Guid.TryParse(value, out var id)
                ? id.ToString("D")
                : Uri.UnescapeDataString(value).Trim();
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizeQuery(IQueryCollection query)
    {
        var normalized = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pair in query)
        {
            normalized[pair.Key] = Array.AsReadOnly(pair.Value.Select(value => value ?? string.Empty).ToArray());
        }

        return normalized;
    }

    private static async Task<string> CanonicalizeBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory);
        request.Body.Position = 0;

        if (memory.Length == 0)
        {
            return EmptyBodyMarker;
        }

        var body = memory.ToArray();
        try
        {
            using var document = JsonDocument.Parse(body);
            using var canonical = new MemoryStream();
            using (var writer = new Utf8JsonWriter(canonical))
            {
                WriteCanonical(writer, document.RootElement);
            }

            return Encoding.UTF8.GetString(canonical.ToArray());
        }
        catch (JsonException)
        {
            return $"<raw>:{Convert.ToHexString(SHA256.HashData(body))}";
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private sealed record FingerprintComponents(
        string Method,
        string Route,
        IReadOnlyDictionary<string, string> RouteValues,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Query,
        string Subject,
        string Body);
}
