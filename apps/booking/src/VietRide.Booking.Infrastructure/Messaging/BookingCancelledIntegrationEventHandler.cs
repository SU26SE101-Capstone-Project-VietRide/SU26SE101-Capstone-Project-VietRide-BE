using MediatR;
using VietRide.Booking.Application.Features.BookingStats.UpdateBookingStats;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class BookingCancelledIntegrationEventHandler
    : IIntegrationEventHandler<BookingCancelledIntegrationEvent>
{
    private readonly IMediator _mediator;

    public BookingCancelledIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        BookingCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        integrationEvent.Validate();

        await _mediator.Send(
            new UpdateBookingStatsCommand(
                BookingCancelledIntegrationEvent.EventType,
                integrationEvent.BookingId!.Value,
                BookingStatsTransition.Cancelled,
                DedupeId: integrationEvent.EventId ?? integrationEvent.BookingId.Value),
            cancellationToken);
    }
}
