using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Payments.ExpirePayment;

public sealed class ExpirePaymentCommandHandler : IRequestHandler<ExpirePaymentCommand, ExpirePaymentResult>
{
    private static readonly TimeSpan PaymentTimeout = TimeSpan.FromMinutes(15);

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
        var legacyCreatedAtOrBefore = now - PaymentTimeout;

        var expiredPayments = await _payments.ExpirePendingRedirectDueAsync(
                legacyCreatedAtOrBefore,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var payment in expiredPayments)
        {
            if (payment.ReferenceType == PaymentReferenceType.SUBSCRIPTION)
            {
                var context = SubscriptionPaymentContextCodec.DeserializeTrusted(payment.Context);
                var subscriptionEvent = new SubscriptionPaymentExpiredIntegrationEvent(
                    payment.Id,
                    payment.ReferenceId,
                    payment.OperatorId ?? Guid.Empty,
                    context.OperatorSubscriptionId);
                await _outbox.EnqueueAsync(
                    subscriptionEvent.EventType,
                    JsonSerializer.Serialize(subscriptionEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var evt = new PaymentExpiredIntegrationEvent(payment.Id, payment.ReferenceType, payment.ReferenceId);
                var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                await _outbox.EnqueueAsync(evt.EventType, payload, cancellationToken).ConfigureAwait(false);
            }
        }

        if (expiredPayments.Count > 0)
        {
            _logger.LogInformation(
                "Expired {PaymentCount} pending VNPay payments due at or before {ExpiredAt}; "
                + "legacy rows used created-at cutoff {LegacyCreatedAtOrBefore}.",
                expiredPayments.Count,
                now,
                legacyCreatedAtOrBefore);
        }

        return new ExpirePaymentResult(expiredPayments.Count);
    }
}
