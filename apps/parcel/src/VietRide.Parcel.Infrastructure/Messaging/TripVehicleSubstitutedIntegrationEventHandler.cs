using MediatR;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class TripVehicleSubstitutedIntegrationEventHandler
    : IIntegrationEventHandler<TripVehicleSubstitutedIntegrationEvent>
{
    private readonly IMediator mediator;

    public TripVehicleSubstitutedIntegrationEventHandler(IMediator mediator)
    {
        this.mediator = mediator;
    }

    public async Task HandleAsync(
        TripVehicleSubstitutedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await mediator.Send(
            new HandleVehicleSubstitutedCommand(
                integrationEvent.OldTripId,
                integrationEvent.NewTripId,
                integrationEvent.OperatorId,
                integrationEvent.Reason),
            cancellationToken);
    }
}
