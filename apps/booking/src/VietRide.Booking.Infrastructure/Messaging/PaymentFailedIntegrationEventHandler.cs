using MediatR;
using VietRide.Booking.Application.Features.Bookings.ExpireBookingOnPayment;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class PaymentFailedIntegrationEventHandler(IMediator mediator)
    : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    public async Task HandleAsync(PaymentFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => await mediator.Send(new ExpireBookingOnPaymentCommand(
            integrationEvent.PaymentId, integrationEvent.ReferenceType, integrationEvent.ReferenceId), cancellationToken);
}
