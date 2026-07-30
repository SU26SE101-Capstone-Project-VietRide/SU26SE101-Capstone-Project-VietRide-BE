using System.Globalization;
using System.Text.Json;
using Polly.CircuitBreaker;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.Dashboard;

namespace VietRide.Booking.Infrastructure.Http;

public sealed class IdentityDashboardMetricsClient : IIdentityDashboardMetricsClient
{
    private readonly HttpClient _client;

    public IdentityDashboardMetricsClient(HttpClient client)
    {
        _client = client;
    }

    public async Task<IdentityDashboardMetricsDto> GetAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"internal/v1/admin/dashboard/identity-metrics?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

        try
        {
            using var response = await _client.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AdminDashboardUnavailableException();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return Parse(document.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AdminDashboardUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or OperationCanceledException
            or JsonException
            or IOException
            or InvalidDataException
            or BrokenCircuitException)
        {
            throw new AdminDashboardUnavailableException(exception);
        }
    }

    private static IdentityDashboardMetricsDto Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !TryNonNegativeInt64(root, "activeUserCount", out var activeUserCount)
            || !root.TryGetProperty("approvedActiveOperatorIds", out var operatorIdsElement)
            || operatorIdsElement.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("userRoleCounts", out var userRolesElement)
            || userRolesElement.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("operatorStatusCounts", out var statusesElement)
            || statusesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Identity dashboard metrics payload is malformed.");
        }

        var operatorIds = ParseOperatorIds(operatorIdsElement);
        var userRoles = ParseCounts(
            userRolesElement,
            "role",
            (key, count) => new IdentityDashboardUserRoleCountDto(key, count));
        var statuses = ParseCounts(
            statusesElement,
            "status",
            (key, count) => new IdentityDashboardOperatorStatusCountDto(key, count));
        return new IdentityDashboardMetricsDto(activeUserCount, operatorIds, userRoles, statuses);
    }

    private static IReadOnlyList<Guid> ParseOperatorIds(JsonElement element)
    {
        var result = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || !Guid.TryParse(item.GetString(), out var id)
                || id == Guid.Empty
                || !seen.Add(id))
            {
                throw new InvalidDataException("Identity dashboard operator IDs are malformed.");
            }

            result.Add(id);
        }

        return result.OrderBy(id => id).ToArray();
    }

    private static IReadOnlyList<T> ParseCounts<T>(
        JsonElement element,
        string keyProperty,
        Func<string, long, T> factory)
    {
        var result = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(keyProperty, out var keyElement)
                || keyElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(keyElement.GetString())
                || !TryNonNegativeInt64(item, "count", out var count)
                || !seen.Add(keyElement.GetString()!))
            {
                throw new InvalidDataException("Identity dashboard distribution is malformed.");
            }

            result.Add(factory(keyElement.GetString()!, count));
        }

        return result;
    }

    private static bool TryNonNegativeInt64(
        JsonElement parent,
        string propertyName,
        out long value)
    {
        value = 0;
        return parent.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out value)
            && value >= 0;
    }
}
