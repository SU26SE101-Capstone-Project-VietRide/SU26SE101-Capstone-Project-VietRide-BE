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
        await _mediator.Send(new HandleTripCancelledCommand(integrationEvent.TripId), cancellationToken);
    }
}
