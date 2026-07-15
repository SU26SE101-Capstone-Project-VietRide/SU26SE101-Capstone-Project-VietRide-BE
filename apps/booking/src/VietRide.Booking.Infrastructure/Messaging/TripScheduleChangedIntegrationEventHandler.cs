using MediatR;
using VietRide.Booking.Application.Features.Bookings.HandleScheduleChange;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class TripScheduleChangedIntegrationEventHandler(IMediator mediator)
    : IIntegrationEventHandler<TripScheduleChangedIntegrationEvent>
{
    public async Task HandleAsync(
        TripScheduleChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        Validate(integrationEvent);
        await mediator.Send(new HandleScheduleChangeCommand(
            integrationEvent.EventId,
            new DateTimeOffset(integrationEvent.OccurredAt),
            integrationEvent.TripId,
            integrationEvent.OperatorId,
            integrationEvent.OldDeparture,
            integrationEvent.NewDeparture,
            integrationEvent.Severity), cancellationToken);
    }

    private static void Validate(TripScheduleChangedIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.EventId == Guid.Empty
            || integrationEvent.TripId == Guid.Empty
            || integrationEvent.OperatorId == Guid.Empty)
        {
            throw new ArgumentException("Trip schedule-changed contract contains an empty required id.");
        }

        if (integrationEvent.OccurredAt == default
            || integrationEvent.OccurredAt.Kind != DateTimeKind.Utc
            || integrationEvent.OldDeparture == default
            || integrationEvent.NewDeparture == default
            || integrationEvent.OldDeparture == integrationEvent.NewDeparture)
        {
            throw new ArgumentException("Trip schedule-changed contract contains an invalid timestamp.");
        }

        if (integrationEvent.Severity is not ("MINOR" or "MEDIUM" or "MAJOR"))
        {
            throw new ArgumentException("Trip schedule-changed contract contains an invalid severity.");
        }
    }
}
