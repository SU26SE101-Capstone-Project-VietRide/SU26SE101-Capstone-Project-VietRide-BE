using MediatR;
using VietRide.Booking.Application.Features.Bookings.ExpireBookingOnPayment;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class PaymentExpiredIntegrationEventHandler
    : IIntegrationEventHandler<PaymentExpiredIntegrationEvent>
{
    private readonly IMediator _mediator;

    public PaymentExpiredIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        PaymentExpiredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new ExpireBookingOnPaymentCommand(
                integrationEvent.PaymentId,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId),
            cancellationToken);
    }
}
