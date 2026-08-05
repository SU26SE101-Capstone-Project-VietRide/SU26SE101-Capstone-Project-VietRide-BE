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
    private readonly IIntegrationEventOutbox outbox;

    public TripRouteChangeService(
        IAlternativeRouteRepository alternativeRoutes,
        IIntegrationEventOutbox outbox)
    {
        this.alternativeRoutes = alternativeRoutes;
        this.outbox = outbox;
    }

    public async Task<TripRouteChangeResult> ApplyAsync(
        Domain.Entities.Trip trip,
        AlternativeRoute alternativeRoute,
        IReadOnlyCollection<Guid> affectedBookingIds,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var candidateStops = await alternativeRoutes.ListCandidateStopsAsync(
            alternativeRoute.Id,
            trip.ActualDepartureTime ?? trip.DepartureDateTime,
            cancellationToken);
        var affectedBookings = affectedBookingIds
            .Distinct()
            .OrderBy(id => id)
            .Select(bookingId => new TripRouteChangedAffectedBooking(bookingId, candidateStops))
            .ToArray();

        if (trip.ChangeAlternativeRoute(alternativeRoute.Id))
        {
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
