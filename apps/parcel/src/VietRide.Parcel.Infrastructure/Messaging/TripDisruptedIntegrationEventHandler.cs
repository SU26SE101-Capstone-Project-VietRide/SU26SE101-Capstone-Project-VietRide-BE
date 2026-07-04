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
        await _mediator.Send(
            new HandleTripDisruptedCommand(
                integrationEvent.TripId,
                integrationEvent.HasSubstitution,
                integrationEvent.TraveledRatio),
            cancellationToken);
    }
}
