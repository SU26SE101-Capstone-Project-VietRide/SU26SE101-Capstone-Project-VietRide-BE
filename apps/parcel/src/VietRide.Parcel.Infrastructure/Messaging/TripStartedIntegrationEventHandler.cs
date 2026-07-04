using MediatR;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class TripStartedIntegrationEventHandler
    : IIntegrationEventHandler<TripStartedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public TripStartedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        TripStartedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(new HandleTripStartedCommand(integrationEvent.TripId), cancellationToken);
    }
}
