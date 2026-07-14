using MediatR;
using VietRide.Booking.Application.Features.Bookings.TripEvents;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class TripCompletedIntegrationEventHandler
    : IIntegrationEventHandler<TripCompletedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public TripCompletedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        TripCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new HandleTripCompletedCommand(
                integrationEvent.TripId,
                integrationEvent.CompletedAt,
                integrationEvent.HasSubstitution),
            cancellationToken);
    }
}
