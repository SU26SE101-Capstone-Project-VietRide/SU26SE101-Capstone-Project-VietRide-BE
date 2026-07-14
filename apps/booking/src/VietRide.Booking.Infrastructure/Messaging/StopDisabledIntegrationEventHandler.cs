using MediatR;
using VietRide.Booking.Application.Features.Bookings.HandleStopDisabled;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class StopDisabledIntegrationEventHandler(IMediator mediator)
    : IIntegrationEventHandler<StopDisabledIntegrationEvent>
{
    public async Task HandleAsync(StopDisabledIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => await mediator.Send(new HandleStopDisabledCommand(
            integrationEvent.EventId, integrationEvent.StopId, integrationEvent.OperatorId,
            integrationEvent.ReplacedByStopId), cancellationToken);
}
