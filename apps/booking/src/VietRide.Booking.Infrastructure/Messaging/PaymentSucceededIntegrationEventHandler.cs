using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Features.Bookings.ConfirmBookingOnPayment;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

internal sealed class PaymentSucceededIntegrationEventHandler
    : IIntegrationEventHandler<PaymentSucceededIntegrationEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<PaymentSucceededIntegrationEventHandler> _logger;

    public PaymentSucceededIntegrationEventHandler(
        IMediator mediator,
        ILogger<PaymentSucceededIntegrationEventHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task HandleAsync(
        PaymentSucceededIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Consuming payment.payment.succeeded {PaymentId} for {ReferenceType}/{ReferenceId}.",
            integrationEvent.PaymentId,
            integrationEvent.ReferenceType,
            integrationEvent.ReferenceId);
        await _mediator.Send(
            new ConfirmBookingOnPaymentCommand(
                integrationEvent.PaymentId,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId,
                integrationEvent.Amount),
            cancellationToken);
    }
}
