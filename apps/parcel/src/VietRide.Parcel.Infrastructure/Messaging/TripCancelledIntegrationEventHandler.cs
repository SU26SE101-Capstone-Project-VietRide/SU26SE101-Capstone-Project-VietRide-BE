using MediatR;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class TripCancelledIntegrationEventHandler
    : IIntegrationEventHandler<TripCancelledIntegrationEvent>
{
    private readonly IMediator _mediator;

    public TripCancelledIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        TripCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        Validate(integrationEvent);
        await _mediator.Send(
            new HandleTripCancelledCommand(
                integrationEvent.EventId,
                new DateTimeOffset(integrationEvent.OccurredAt),
                integrationEvent.TripId,
                integrationEvent.OperatorId,
                integrationEvent.CancelledAt,
                integrationEvent.CancelReason),
            cancellationToken);
    }

    private static void Validate(TripCancelledIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.EventId == Guid.Empty
            || integrationEvent.TripId == Guid.Empty
            || integrationEvent.OperatorId == Guid.Empty)
        {
            throw new ArgumentException("Trip-cancelled contract contains an empty required id.");
        }

        if (integrationEvent.OccurredAt == default
            || integrationEvent.OccurredAt.Kind != DateTimeKind.Utc
            || integrationEvent.CancelledAt == default
            || string.IsNullOrWhiteSpace(integrationEvent.CancelReason))
        {
            throw new ArgumentException("Trip-cancelled contract contains invalid required data.");
        }
    }
}
