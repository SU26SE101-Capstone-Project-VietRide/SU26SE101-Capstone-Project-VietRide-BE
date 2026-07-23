using MediatR;
using VietRide.Booking.Application.Features.Bookings.HandleTripCancelled;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class TripCancelledByOperatorIntegrationEventHandler(IMediator mediator)
    : IIntegrationEventHandler<TripCancelledByOperatorIntegrationEvent>
{
    public async Task HandleAsync(
        TripCancelledByOperatorIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        Validate(integrationEvent);
        await mediator.Send(new HandleTripCancelledCommand(
            integrationEvent.EventId,
            new DateTimeOffset(integrationEvent.OccurredAt),
            integrationEvent.TripId,
            integrationEvent.OperatorId,
            integrationEvent.CancelledAt,
            integrationEvent.CancelReason), cancellationToken);
    }

    private static void Validate(TripCancelledByOperatorIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.EventId == Guid.Empty
            || integrationEvent.TripId == Guid.Empty
            || integrationEvent.OperatorId == Guid.Empty)
        {
            throw new ArgumentException("Trip-cancelled contract contains an empty required id.");
        }

        var occurredAt = new DateTimeOffset(integrationEvent.OccurredAt);
        if (integrationEvent.OccurredAt == default
            || integrationEvent.OccurredAt.Kind != DateTimeKind.Utc
            || integrationEvent.CancelledAt == default
            || integrationEvent.CancelledAt != occurredAt)
        {
            throw new ArgumentException("Trip-cancelled contract contains invalid or inconsistent timestamps.");
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.CancelReason))
        {
            throw new ArgumentException("Trip-cancelled contract requires a cancellation reason.");
        }
    }
}
