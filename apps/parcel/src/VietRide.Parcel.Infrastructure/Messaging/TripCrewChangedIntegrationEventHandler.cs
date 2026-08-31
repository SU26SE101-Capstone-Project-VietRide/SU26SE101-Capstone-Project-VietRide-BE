using MediatR;
using VietRide.Parcel.Application.Features.Reliability.ApprovalRequests;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class TripCrewChangedIntegrationEventHandler
    : IIntegrationEventHandler<TripCrewChangedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public TripCrewChangedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        TripCrewChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.TripId == Guid.Empty
            || integrationEvent.OperatorId == Guid.Empty
            || integrationEvent.DepartureDateTime == default)
            throw new ArgumentException("Trip crew-changed contract contains invalid required data.");

        await _mediator.Send(
            new HandleTripCrewChangedCommand(
                integrationEvent.TripId,
                integrationEvent.OperatorId,
                integrationEvent.OldDriverUserId,
                integrationEvent.DriverUserId),
            cancellationToken);
    }
}
