using System.Text.Json;
using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Internal.Payments.ExpireSubscriptionPayment;

public sealed class ExpireSubscriptionPaymentCommandHandler
    : IRequestHandler<ExpireSubscriptionPaymentCommand, bool>
{
    private readonly IPaymentRepository _payments;
    private readonly IClock _clock;
    private readonly IIntegrationEventOutbox _outbox;

    public ExpireSubscriptionPaymentCommandHandler(
        IPaymentRepository payments,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _payments = payments;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<bool> Handle(ExpireSubscriptionPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await _payments.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new CodedNotFoundException("PAYMENT_NOT_FOUND", "Subscription payment was not found.");
        if (payment.ReferenceType != PaymentReferenceType.SUBSCRIPTION)
            throw new CodedValidationException("VALIDATION_ERROR", "Payment is not a subscription payment.");
        if (payment.Status == PaymentStatus.EXPIRED)
            return false;
        if (payment.Status != PaymentStatus.PENDING_REDIRECT)
            throw new CodedConflictException("PAYMENT_ALREADY_PROCESSED", "Subscription payment is no longer pending.");

        payment.MarkExpired(_clock.UtcNow);
        _payments.Update(payment);
        var context = SubscriptionPaymentContextCodec.DeserializeTrusted(payment.Context);
        var evt = new SubscriptionPaymentExpiredIntegrationEvent(
            payment.Id,
            payment.ReferenceId,
            payment.OperatorId ?? Guid.Empty,
            context.OperatorSubscriptionId);
        await _outbox.EnqueueAsync(
            evt.EventType,
            JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
