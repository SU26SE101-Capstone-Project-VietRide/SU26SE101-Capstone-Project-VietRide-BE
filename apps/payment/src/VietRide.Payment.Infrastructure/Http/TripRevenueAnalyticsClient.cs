using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Http;

internal sealed class TripRevenueAnalyticsClient : ITripRevenueAnalyticsClient
{
    private readonly HttpClient client;
    private readonly IInternalJwtTokenProvider tokens;

    public TripRevenueAnalyticsClient(HttpClient client, IInternalJwtTokenProvider tokens)
    {
        this.client = client;
        this.tokens = tokens;
    }

    public async Task<IReadOnlyList<TripVehicleCountItem>> GetVehicleCountsAsync(
        IReadOnlyList<Guid> operatorIds,
        CancellationToken cancellationToken = default)
    {
        ValidateBatch(operatorIds, 100, nameof(operatorIds), allowEmpty: false);
        using var request = PlatformReportHttpClientSupport.CreateRequest(
            HttpMethod.Post,
            "internal/v1/operators/vehicle-counts/batch",
            tokens,
            JsonContent.Create(new { operatorIds }));
        using var response = await PlatformReportHttpClientSupport.SendAsync(client, request, cancellationToken);
        await PlatformReportHttpClientSupport.EnsureSuccessAsync(response, false, cancellationToken);
        using var document = await PlatformReportHttpClientSupport.ReadJsonAsync(response, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new UpstreamUnavailableException();
        }

        var requested = operatorIds.ToHashSet();
        var seen = new HashSet<Guid>();
        var result = new List<TripVehicleCountItem>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!PlatformReportHttpClientSupport.TryGuid(item, "operatorId", out var operatorId)
                || !requested.Contains(operatorId)
                || !seen.Add(operatorId)
                || !TryNonNegativeInt(item, "vehicleCount", out var vehicleCount))
            {
                throw new UpstreamUnavailableException();
            }

            result.Add(new TripVehicleCountItem(operatorId, vehicleCount));
        }

        if (seen.Count != requested.Count)
        {
            throw new UpstreamUnavailableException();
        }

        return result.OrderBy(item => item.OperatorId).ToArray();
    }

    public async Task<IReadOnlyList<TripRoutePerformanceItem>> GetRoutePerformanceAsync(
        Guid operatorId,
        string month,
        CancellationToken cancellationToken = default)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator id must be non-empty.", nameof(operatorId));
        }

        _ = RevenueAnalyticsPeriodRules.OperatorMonth(month);
        using var request = PlatformReportHttpClientSupport.CreateRequest(
            HttpMethod.Get,
            $"internal/v1/operators/{operatorId:D}/route-performance?month={Uri.EscapeDataString(month)}",
            tokens);
        using var response = await PlatformReportHttpClientSupport.SendAsync(client, request, cancellationToken);
        await PlatformReportHttpClientSupport.EnsureSuccessAsync(response, false, cancellationToken);
        using var document = await PlatformReportHttpClientSupport.ReadJsonAsync(response, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new UpstreamUnavailableException();
        }

        var seen = new HashSet<Guid>();
        var result = new List<TripRoutePerformanceItem>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!PlatformReportHttpClientSupport.TryGuid(item, "routeId", out var routeId)
                || !seen.Add(routeId)
                || !TryRequiredString(item, "routeName", out var routeName)
                || !TryRequiredString(item, "originName", out var originName)
                || !TryRequiredString(item, "destinationName", out var destinationName)
                || !TryNonNegativeInt(item, "tripCount", out var tripCount)
                || !TryNonNegativeInt(item, "completedTripCount", out var completedTripCount)
                || completedTripCount > tripCount)
            {
                throw new UpstreamUnavailableException();
            }

            result.Add(new TripRoutePerformanceItem(
                routeId,
                routeName,
                originName,
                destinationName,
                tripCount,
                completedTripCount));
        }

        return result
            .OrderBy(item => item.RouteName, StringComparer.Ordinal)
            .ThenBy(item => item.RouteId)
            .ToArray();
    }

    public async Task<IReadOnlyList<TripRevenueSummaryItem>> GetTripSummariesAsync(
        IReadOnlyList<Guid> tripIds,
        CancellationToken cancellationToken = default)
    {
        ValidateBatch(tripIds, int.MaxValue, nameof(tripIds), allowEmpty: true);
        if (tripIds.Count == 0)
        {
            return [];
        }

        var result = new List<TripRevenueSummaryItem>(tripIds.Count);
        var seen = new HashSet<Guid>();
        foreach (var chunk in tripIds.Chunk(100))
        {
            using var request = PlatformReportHttpClientSupport.CreateRequest(
                HttpMethod.Post,
                "internal/v1/trips/summaries/batch",
                tokens,
                JsonContent.Create(new { tripIds = chunk }));
            using var response = await PlatformReportHttpClientSupport.SendAsync(client, request, cancellationToken);
            await PlatformReportHttpClientSupport.EnsureSuccessAsync(response, false, cancellationToken);
            using var document = await PlatformReportHttpClientSupport.ReadJsonAsync(response, cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new UpstreamUnavailableException();
            }

            var requestedChunk = chunk.ToHashSet();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!TryTripSummary(item, requestedChunk, seen, out var summary))
                {
                    throw new UpstreamUnavailableException();
                }

                result.Add(summary);
            }
        }

        if (seen.Count != tripIds.Count)
        {
            throw new UpstreamUnavailableException();
        }

        return result.OrderBy(item => item.TripId).ToArray();
    }

    private static bool TryTripSummary(
        JsonElement item,
        IReadOnlySet<Guid> requestedChunk,
        ISet<Guid> seen,
        out TripRevenueSummaryItem summary)
    {
        summary = default!;
        if (!PlatformReportHttpClientSupport.TryGuid(item, "tripId", out var tripId)
            || !requestedChunk.Contains(tripId)
            || !seen.Add(tripId)
            || !TryRequiredString(item, "status", out var status)
            || !item.TryGetProperty("departureAt", out var departureProperty)
            || departureProperty.ValueKind != JsonValueKind.String
            || !departureProperty.TryGetDateTimeOffset(out var departureAt)
            || !item.TryGetProperty("route", out var route)
            || !PlatformReportHttpClientSupport.TryGuid(route, "routeId", out var routeId)
            || !TryRequiredString(route, "name", out var routeName)
            || !TryRequiredString(route, "originName", out var originName)
            || !TryRequiredString(route, "destinationName", out var destinationName))
        {
            return false;
        }

        summary = new TripRevenueSummaryItem(
            tripId,
            status,
            departureAt,
            routeId,
            routeName,
            originName,
            destinationName,
            OptionalString(item, "tripCode"),
            OptionalString(route, "code"));
        return true;
    }

    private static string? OptionalString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString())
                ? property.GetString()
                : null;

    private static void ValidateBatch(
        IReadOnlyList<Guid> ids,
        int maximum,
        string parameterName,
        bool allowEmpty)
    {
        if ((!allowEmpty && ids.Count == 0)
            || ids.Count > maximum
            || ids.Any(id => id == Guid.Empty)
            || ids.Distinct().Count() != ids.Count)
        {
            throw new ArgumentException(
                $"{parameterName} must be distinct, non-empty UUIDs within the supported batch size.",
                parameterName);
        }
    }

    private static bool TryNonNegativeInt(JsonElement item, string propertyName, out int value)
    {
        value = default;
        return item.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value)
            && value >= 0;
    }

    private static bool TryRequiredString(JsonElement item, string propertyName, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }
}
