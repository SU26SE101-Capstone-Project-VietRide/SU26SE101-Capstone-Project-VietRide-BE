using MediatR;
using VietRide.Booking.Application.Features.PendingActions;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class TripRouteChangedIntegrationEventHandler(IMediator mediator)
    : IIntegrationEventHandler<TripRouteChangedIntegrationEvent>
{
    public Task HandleAsync(
        TripRouteChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => mediator.Send(new CreateRouteChangePendingActionCommand(
            integrationEvent.EventId,
            new DateTimeOffset(integrationEvent.OccurredAt),
            integrationEvent.TripId,
            integrationEvent.OperatorId,
            integrationEvent.TripStatus,
            integrationEvent.AlternativeRouteId,
            integrationEvent.AffectedBookings
                .Select(affected => new RouteChangeAffectedBooking(
                    affected.BookingId,
                    affected.CandidateStops
                        .Select(candidate => new RouteChangeCandidateStop(
                            candidate.StopId,
                            candidate.StationId,
                            candidate.StationName,
                            candidate.Sequence,
                            candidate.EstimatedArrivalAt))
                        .ToArray()))
                .ToArray()), cancellationToken);
}
