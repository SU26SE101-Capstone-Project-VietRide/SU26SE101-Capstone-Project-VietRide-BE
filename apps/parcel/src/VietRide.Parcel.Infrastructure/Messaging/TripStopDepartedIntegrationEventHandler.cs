using MediatR;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class TripStopDepartedIntegrationEventHandler
    : IIntegrationEventHandler<TripStopDepartedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public TripStopDepartedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public Task HandleAsync(
        TripStopDepartedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => _mediator.Send(
            new HandleTripStopDepartedWithPendingCommand(
                integrationEvent.TripId,
                integrationEvent.StopId,
                integrationEvent.DepartedAt),
            cancellationToken);
}
