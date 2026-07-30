using MediatR;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class TripDisruptedIntegrationEventHandler
    : IIntegrationEventHandler<TripDisruptedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public TripDisruptedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        TripDisruptedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        Validate(integrationEvent);
        await _mediator.Send(
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
            throw new ArgumentException("Trip-disrupted contract contains invalid required data.");
        }
    }
}
