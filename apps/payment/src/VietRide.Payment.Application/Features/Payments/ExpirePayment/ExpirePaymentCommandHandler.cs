using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Payments.ExpirePayment;

public sealed class ExpirePaymentCommandHandler : IRequestHandler<ExpirePaymentCommand, ExpirePaymentResult>
{
    private static readonly TimeSpan PaymentTimeout = TimeSpan.FromMinutes(10);

    private readonly IPaymentRepository _payments;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<ExpirePaymentCommandHandler> _logger;

    public ExpirePaymentCommandHandler(
        IPaymentRepository payments,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<ExpirePaymentCommandHandler> logger)
    {
        _payments = payments;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ExpirePaymentResult> Handle(
        ExpirePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var now = request.Now ?? _clock.UtcNow;
        var expiresBefore = now - PaymentTimeout;

        var expiredPayments = await _payments.ExpirePendingRedirectOlderThanAsync(
                expiresBefore,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var payment in expiredPayments)
        {
            var evt = new PaymentExpiredIntegrationEvent(payment.Id, payment.ReferenceType, payment.ReferenceId);
            var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await _outbox.EnqueueAsync(evt.EventType, payload, cancellationToken).ConfigureAwait(false);
        }

        if (expiredPayments.Count > 0)
        {
            _logger.LogInformation(
                "Expired {PaymentCount} pending VNPay payments older than {ExpiresBefore}.",
                expiredPayments.Count,
                expiresBefore);
        }

        return new ExpirePaymentResult(expiredPayments.Count);
    }
}
