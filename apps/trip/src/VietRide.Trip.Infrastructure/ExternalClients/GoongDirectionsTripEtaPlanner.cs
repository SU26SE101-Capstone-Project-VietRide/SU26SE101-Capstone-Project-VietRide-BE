using System.Globalization;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.ExternalClients;

internal sealed class GoongDirectionsTripEtaPlanner : ITripEtaPlanner
{
    private readonly GoongDirectionsClient directionsClient;
    private readonly int dwellMinutes;

    public GoongDirectionsTripEtaPlanner(HttpClient httpClient, IConfiguration configuration)
    {
        var timeoutMs = int.TryParse(
            configuration["TRIP_PLANNED_ETA_TIMEOUT_MS"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredTimeout)
            ? configuredTimeout
            : 3_000;
        httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs));
        directionsClient = new GoongDirectionsClient(httpClient, configuration);
        dwellMinutes = int.TryParse(
            configuration["TRIP_STOP_DWELL_MINUTES"],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var configuredDwell)
            ? Math.Max(0, configuredDwell)
            : 20;
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
        if (!directionsClient.IsConfigured
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
                new GoongCoordinate(item.Stop.Latitude, item.Stop.Longitude),
                IsDestination: false))
            .Append(new EtaTarget(
                destinationStation.Id,
                new GoongCoordinate(destinationLatitude, destinationLongitude),
                IsDestination: true))
            .ToArray();

        try
        {
            var stopArrivals = new Dictionary<Guid, DateTimeOffset>();
            var cumulativeDrive = TimeSpan.Zero;
            var targetOffset = 0;
            var chunkOrigin = new GoongCoordinate(originLatitude, originLongitude);

            while (targetOffset < targets.Length)
            {
                var chunk = targets
                    .Skip(targetOffset)
                    .Take(directionsClient.MaxDestinationsPerRequest)
                    .ToArray();
                var legs = await directionsClient.GetLegsAsync(
                    chunkOrigin,
                    chunk.Select(target => target.Coordinate).ToArray(),
                    cancellationToken).ConfigureAwait(false);
                if (legs is null || legs.Count != chunk.Length)
                {
                    return fallback;
                }

                for (var index = 0; index < chunk.Length; index++)
                {
                    cumulativeDrive += TimeSpan.FromSeconds(legs[index].DurationSeconds);
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
                    chunkOrigin = chunk[^1].Coordinate;
                }
            }

            var destinationArrival = departureTime
                + cumulativeDrive
                + TimeSpan.FromMinutes((long)dwellMinutes * orderedStops.Length);
            return new TripEtaPlan(PlannedEtaSource.GOONG, destinationArrival, stopArrivals);
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

    private sealed record EtaTarget(
        Guid Id,
        GoongCoordinate Coordinate,
        bool IsDestination);
}
