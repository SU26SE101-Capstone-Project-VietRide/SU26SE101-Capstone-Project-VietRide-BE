using MediatR;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class TripDestinationArrivedIntegrationEventHandler
    : IIntegrationEventHandler<TripDestinationArrivedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public TripDestinationArrivedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public Task HandleAsync(
        TripDestinationArrivedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => _mediator.Send(
            new HandleTripDestinationArrivedCommand(
                integrationEvent.TripId,
                integrationEvent.DestinationStationId,
                integrationEvent.ActualArrivalTime),
            cancellationToken);
}
