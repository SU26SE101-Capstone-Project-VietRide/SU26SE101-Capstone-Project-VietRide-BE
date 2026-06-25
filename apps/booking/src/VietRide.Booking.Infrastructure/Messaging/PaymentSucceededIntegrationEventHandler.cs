using MediatR;
using VietRide.Booking.Application.Features.Bookings.ConfirmBookingOnPayment;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class PaymentSucceededIntegrationEventHandler
    : IIntegrationEventHandler<PaymentSucceededIntegrationEvent>
{
    private readonly IMediator _mediator;

    public PaymentSucceededIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        PaymentSucceededIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new ConfirmBookingOnPaymentCommand(
                integrationEvent.PaymentId,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId,
                integrationEvent.Amount),
            cancellationToken);
    }
}
