using MediatR;
using VietRide.Booking.Application.Features.Bookings.HandleTripDisrupted;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class TripDisruptedIntegrationEventHandler(IMediator mediator)
    : IIntegrationEventHandler<TripDisruptedIntegrationEvent>
{
    public async Task HandleAsync(
        TripDisruptedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        Validate(integrationEvent);
        await mediator.Send(
            new HandleTripDisruptedCommand(
                integrationEvent.EventId,
                new DateTimeOffset(integrationEvent.OccurredAt),
                integrationEvent.TripId,
                integrationEvent.OperatorId,
                integrationEvent.TerminalAt,
                integrationEvent.HasSubstitution,
                integrationEvent.Reason),
            cancellationToken);
    }

    private static void Validate(TripDisruptedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.EventId == Guid.Empty
            || integrationEvent.TripId == Guid.Empty
            || integrationEvent.OperatorId == Guid.Empty)
        {
            throw new ArgumentException("Trip-disrupted contract contains an empty required id.");
        }

        if (integrationEvent.OccurredAt == default
            || integrationEvent.OccurredAt.Kind != DateTimeKind.Utc
            || integrationEvent.TerminalAt == default)
        {
            throw new ArgumentException("Trip-disrupted contract contains an invalid timestamp.");
        }

        if (integrationEvent.Reason?.Length > 500)
        {
            throw new ArgumentException("Trip-disrupted reason cannot exceed 500 characters.");
        }
    }
}
