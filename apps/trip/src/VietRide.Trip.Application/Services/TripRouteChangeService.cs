using System.Text.Json;
using VietRide.Shared.Application.Outbox;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Services;

public sealed class TripRouteChangeService : ITripRouteChangeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAlternativeRouteRepository alternativeRoutes;
    private readonly ITripStopRepository tripStops;
    private readonly IIntegrationEventOutbox outbox;

    public TripRouteChangeService(
        IAlternativeRouteRepository alternativeRoutes,
        ITripStopRepository tripStops,
        IIntegrationEventOutbox outbox)
    {
        this.alternativeRoutes = alternativeRoutes;
        this.tripStops = tripStops;
        this.outbox = outbox;
    }

    public async Task<TripRouteChangeResult> ApplyAsync(
        Domain.Entities.Trip trip,
        AlternativeRoute alternativeRoute,
        IReadOnlyCollection<Guid> affectedBookingIds,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var estimatedArrivalBase = trip.ActualDepartureTime ?? trip.DepartureDateTime;
        var candidateStops = await alternativeRoutes.ListCandidateStopsAsync(
            alternativeRoute.Id,
            estimatedArrivalBase,
            cancellationToken);
        var affectedBookings = affectedBookingIds
            .Distinct()
            .OrderBy(id => id)
            .Select(bookingId => new TripRouteChangedAffectedBooking(bookingId, candidateStops))
            .ToArray();

        if (trip.ChangeAlternativeRoute(alternativeRoute.Id))
        {
            var alternativeRouteStops = await alternativeRoutes.ListStopsAsync(
                alternativeRoute.Id,
                cancellationToken);
            var existingTripStops = await tripStops.AcquireByTripAsync(
                trip.Id,
                cancellationToken);
            var arrivedTripStops = existingTripStops
                .Where(stop => stop.Status == TripStopStatus.ARRIVED)
                .OrderBy(stop => stop.OrderIndex)
                .ThenBy(stop => stop.StopId)
                .ToArray();
            var arrivedStopIds = arrivedTripStops
                .Select(stop => stop.StopId)
                .ToHashSet();
            await tripStops.DeleteNonArrivedByTripAsync(trip.Id, cancellationToken);

            var nextOrderIndex = arrivedTripStops.Length == 0
                ? 1
                : checked(arrivedTripStops.Max(stop => stop.OrderIndex) + 1);
            foreach (var alternativeRouteStop in alternativeRouteStops
                         .Where(stop => !arrivedStopIds.Contains(stop.StopId))
                         .OrderBy(stop => stop.OrderIndex)
                         .ThenBy(stop => stop.StopId))
            {
                await tripStops.AddAsync(
                    TripStop.Create(
                        trip.Id,
                        alternativeRouteStop.StopId,
                        nextOrderIndex++,
                        estimatedArrivalBase.AddMinutes(
                            alternativeRouteStop.EstimatedDurationFromOriginMinutes),
                        allowPickup: true,
                        allowDropoff: true,
                        distanceFromOriginKm: alternativeRouteStop.DistanceFromOriginKm),
                    cancellationToken);
            }

            var estimatedDestinationArrival = candidateStops
                .LastOrDefault(stop => stop.StationId == alternativeRoute.DestinationStationId)
                ?.EstimatedArrivalAt
                ?? estimatedArrivalBase.AddMinutes(
                    alternativeRoute.EstimatedDurationMinutes
                    ?? alternativeRouteStops.LastOrDefault()?.EstimatedDurationFromOriginMinutes
                    ?? 0);
            if (estimatedDestinationArrival <= estimatedArrivalBase)
            {
                estimatedDestinationArrival = trip.EstimatedArrivalTime > estimatedArrivalBase
                    ? trip.EstimatedArrivalTime
                    : estimatedArrivalBase.AddMinutes(1);
            }

            trip.RecomputeAlternativeRoutePlannedArrival(estimatedDestinationArrival);

            var evt = new TripRouteChangedIntegrationEvent(
                trip.Id,
                trip.OperatorId,
                trip.Status.ToString(),
                alternativeRoute.Id,
                affectedBookings,
                occurredAt);
            await outbox.EnqueueAsync(
                evt.EventId,
                evt.EventType,
                JsonSerializer.Serialize(evt, JsonOptions),
                cancellationToken);
        }

        return new TripRouteChangeResult(
            trip.Id,
            trip.Status.ToString(),
            alternativeRoute.Id,
            affectedBookings);
    }
}
