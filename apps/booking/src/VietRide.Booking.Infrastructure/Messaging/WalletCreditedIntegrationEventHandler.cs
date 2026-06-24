using MediatR;
using VietRide.Booking.Application.Features.Bookings.MarkBookingRefunded;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class WalletCreditedIntegrationEventHandler
    : IIntegrationEventHandler<WalletCreditedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public WalletCreditedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        WalletCreditedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new MarkBookingRefundedCommand(
                integrationEvent.UserId,
                integrationEvent.Amount,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId),
            cancellationToken);
    }
}
