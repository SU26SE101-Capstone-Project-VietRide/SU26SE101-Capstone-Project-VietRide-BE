using MediatR;
using VietRide.Booking.Application.Features.BookingStats.UpdateBookingStats;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class BookingRefundedIntegrationEventHandler
    : IIntegrationEventHandler<BookingRefundedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public BookingRefundedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        BookingRefundedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new UpdateBookingStatsCommand(
                BookingRefundedIntegrationEvent.EventType,
                integrationEvent.BookingId,
                BookingStatsTransition.Refunded,
                integrationEvent.Amount),
            cancellationToken);
    }
}
