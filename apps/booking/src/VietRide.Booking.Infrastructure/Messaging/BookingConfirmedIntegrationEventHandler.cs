using MediatR;
using VietRide.Booking.Application.Features.BookingStats.UpdateBookingStats;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class BookingConfirmedIntegrationEventHandler
    : IIntegrationEventHandler<BookingConfirmedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public BookingConfirmedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        BookingConfirmedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new UpdateBookingStatsCommand(
                BookingConfirmedIntegrationEvent.EventType,
                integrationEvent.BookingId,
                BookingStatsTransition.Confirmed,
                integrationEvent.TotalAmount),
            cancellationToken);
    }
}
