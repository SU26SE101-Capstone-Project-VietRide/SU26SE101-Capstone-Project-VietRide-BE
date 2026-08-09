using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.ExternalClients;

internal sealed class GoogleRoutesTripEtaPlanner : ITripEtaPlanner
{
    private const int MaxTargetsPerRequest = 26;
    private readonly HttpClient httpClient;
    private readonly bool enabled;
    private readonly string apiKey;
    private readonly int dwellMinutes;

    public GoogleRoutesTripEtaPlanner(HttpClient httpClient, IConfiguration configuration)
    {
        this.httpClient = httpClient;
        enabled = bool.TryParse(configuration["GOOGLE_ROUTES_ENABLED"], out var configured) && configured;
        apiKey = configuration["GOOGLE_ROUTES_API_KEY"] ?? string.Empty;
        dwellMinutes = int.TryParse(
            configuration["TRIP_STOP_DWELL_MINUTES"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredDwell)
            ? Math.Max(0, configuredDwell)
            : 20;
        var timeoutMs = int.TryParse(
            configuration["TRIP_PLANNED_ETA_TIMEOUT_MS"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredTimeout)
            ? configuredTimeout
            : 3_000;
        httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
    }

    public async Task<TripEtaPlan> PlanAsync(
        Route route,
        Station originStation,
        Station destinationStation,
        IReadOnlyList<TripEtaStopInput> stops,
        DateTimeOffset departureTime,
        CancellationToken cancellationToken = default)
    {
        var orderedStops = stops.OrderBy(item => item.RouteStop.OrderIndex).ToArray();
        var fallback = BuildFallback(route, orderedStops, departureTime);
        if (!enabled
            || string.IsNullOrWhiteSpace(apiKey)
            || originStation.Latitude is not decimal originLatitude
            || originStation.Longitude is not decimal originLongitude
            || destinationStation.Latitude is not decimal destinationLatitude
            || destinationStation.Longitude is not decimal destinationLongitude)
        {
            return fallback;
        }

        var targets = orderedStops
            .Select(item => new EtaTarget(
                item.RouteStop.StopId,
                item.Stop.Latitude,
                item.Stop.Longitude,
                IsDestination: false))
            .Append(new EtaTarget(
                destinationStation.Id,
                destinationLatitude,
                destinationLongitude,
                IsDestination: true))
            .ToArray();

        try
        {
            var stopArrivals = new Dictionary<Guid, DateTimeOffset>();
            var cumulativeDrive = TimeSpan.Zero;
            var targetOffset = 0;
            var chunkOrigin = new Coordinate(originLatitude, originLongitude);
            var chunkDeparture = departureTime;

            while (targetOffset < targets.Length)
            {
                var chunk = targets.Skip(targetOffset).Take(MaxTargetsPerRequest).ToArray();
                var durations = await ComputeChunkAsync(
                    chunkOrigin,
                    chunk,
                    chunkDeparture,
                    cancellationToken).ConfigureAwait(false);
                if (durations is null || durations.Count != chunk.Length)
                {
                    return fallback;
                }

                for (var index = 0; index < chunk.Length; index++)
                {
                    cumulativeDrive += durations[index];
                    var globalIndex = targetOffset + index;
                    var arrival = departureTime
                        + cumulativeDrive
                        + TimeSpan.FromMinutes((long)dwellMinutes * globalIndex);
                    if (!chunk[index].IsDestination)
                    {
                        stopArrivals[chunk[index].Id] = arrival;
                    }
                }

                targetOffset += chunk.Length;
                if (targetOffset < targets.Length)
                {
                    var boundary = chunk[^1];
                    chunkOrigin = new Coordinate(boundary.Latitude, boundary.Longitude);
                    chunkDeparture = departureTime
                        + cumulativeDrive
                        + TimeSpan.FromMinutes((long)dwellMinutes * targetOffset);
                }
            }

            var destinationArrival = departureTime
                + cumulativeDrive
                + TimeSpan.FromMinutes((long)dwellMinutes * orderedStops.Length);
            return new TripEtaPlan(PlannedEtaSource.GOOGLE_ROUTES, destinationArrival, stopArrivals);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return fallback;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private async Task<IReadOnlyList<TimeSpan>?> ComputeChunkAsync(
        Coordinate origin,
        IReadOnlyList<EtaTarget> targets,
        DateTimeOffset departureTime,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "directions/v2:computeRoutes")
        {
            Content = JsonContent.Create(new
            {
                origin = ToWaypoint(origin.Latitude, origin.Longitude),
                destination = ToWaypoint(targets[^1].Latitude, targets[^1].Longitude),
                intermediates = targets
                    .Take(targets.Count - 1)
                    .Select(target => ToWaypoint(target.Latitude, target.Longitude, vehicleStopover: true))
                    .ToArray(),
                travelMode = "DRIVE",
                routingPreference = "TRAFFIC_AWARE",
                departureTime = departureTime > DateTimeOffset.UtcNow
                    ? departureTime.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
                    : null,
                computeAlternativeRoutes = false,
                optimizeWaypointOrder = false,
                units = "METRIC",
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", apiKey);
        request.Headers.TryAddWithoutValidation("X-Goog-FieldMask", "routes.legs.duration");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<RoutesResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var legs = payload?.Routes?.FirstOrDefault()?.Legs;
        if (legs is null || legs.Count != targets.Count)
        {
            return null;
        }

        var durations = new List<TimeSpan>(legs.Count);
        foreach (var leg in legs)
        {
            if (!TryParseDuration(leg.Duration, out var duration))
            {
                return null;
            }

            durations.Add(duration);
        }

        return durations;
    }

    private TripEtaPlan BuildFallback(
        Route route,
        IReadOnlyList<TripEtaStopInput> stops,
        DateTimeOffset departureTime)
    {
        var arrivals = stops
            .Select((item, index) => new
            {
                item.RouteStop.StopId,
                Arrival = departureTime
                    .AddMinutes(item.RouteStop.EstimatedDurationFromOriginMinutes)
                    .AddMinutes((long)dwellMinutes * index),
            })
            .ToDictionary(item => item.StopId, item => item.Arrival);
        var driveDuration = route.EstimatedDurationMinutes
            ?? stops.Select(item => item.RouteStop.EstimatedDurationFromOriginMinutes).DefaultIfEmpty(1).Max();
        driveDuration = Math.Max(1, driveDuration);
        var destinationArrival = departureTime
            .AddMinutes(driveDuration)
            .AddMinutes((long)dwellMinutes * stops.Count);
        return new TripEtaPlan(PlannedEtaSource.ROUTE_BASELINE, destinationArrival, arrivals);
    }

    private static Dictionary<string, object> ToWaypoint(
        decimal latitude,
        decimal longitude,
        bool vehicleStopover = false)
    {
        var waypoint = new Dictionary<string, object>
        {
            ["location"] = new
            {
                latLng = new { latitude, longitude },
            },
        };
        if (vehicleStopover)
        {
            waypoint["vehicleStopover"] = true;
        }

        return waypoint;
    }

    private static bool TryParseDuration(string? value, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(value) || !value.EndsWith('s'))
        {
            return false;
        }

        if (!double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds)
            || seconds < 0)
        {
            return false;
        }

        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private sealed record Coordinate(decimal Latitude, decimal Longitude);

    private sealed record EtaTarget(
        Guid Id,
        decimal Latitude,
        decimal Longitude,
        bool IsDestination);

    private sealed record RoutesResponse(
        [property: JsonPropertyName("routes")] IReadOnlyList<RouteResponse>? Routes);

    private sealed record RouteResponse(
        [property: JsonPropertyName("legs")] IReadOnlyList<RouteLegResponse>? Legs);

    private sealed record RouteLegResponse(
        [property: JsonPropertyName("duration")] string? Duration);
}
