using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;

public sealed class CreateSubscriptionPaymentCommandHandler
    : IRequestHandler<CreateSubscriptionPaymentCommand, CreateSubscriptionPaymentResult>
{
    private readonly IPaymentRepository _payments;
    private readonly IVnPayClient _vnPayClient;
    private readonly IClock _clock;

    public CreateSubscriptionPaymentCommandHandler(
        IPaymentRepository payments,
        IVnPayClient vnPayClient,
        IClock clock)
    {
        _payments = payments;
        _vnPayClient = vnPayClient;
        _clock = clock;
    }

    public async Task<CreateSubscriptionPaymentResult> Handle(
        CreateSubscriptionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var replay = await _payments.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (replay is not null)
        {
            EnsureReplayMatches(replay, request);
            return ToResult(replay);
        }

        await _payments.AcquirePaymentReferenceLockAsync(
                PaymentReferenceType.SUBSCRIPTION,
                request.UpgradeAttemptId,
                cancellationToken)
            .ConfigureAwait(false);

        var existing = await _payments.FindByReferenceAsync(
                PaymentReferenceType.SUBSCRIPTION,
                request.UpgradeAttemptId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureReplayMatches(existing, request);
            return ToResult(existing);
        }

        var amount = Money.FromRaw(request.Amount);
        var now = _clock.UtcNow;
        var txnRef = Guid.NewGuid().ToString("D");
        var redirectUrl = _vnPayClient.CreateSubscriptionPaymentRedirectUrl(
            request.UpgradeAttemptId,
            request.OperatorId,
            amount,
            txnRef,
            request.ClientIpAddress,
            now);
        var payment = PaymentEntity.CreatePendingRedirectVnPaySubscription(
            request.UpgradeAttemptId,
            request.OperatorId,
            amount,
            txnRef,
            request.IdempotencyKey,
            redirectUrl);

        await _payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);
        return ToResult(payment);
    }

    private static CreateSubscriptionPaymentResult ToResult(PaymentEntity payment)
    {
        if (string.IsNullOrWhiteSpace(payment.PaymentRedirectUrl))
            throw new InvalidOperationException("Subscription payment redirect URL is missing.");

        return new CreateSubscriptionPaymentResult(payment.Id, payment.Status.ToString(), payment.PaymentRedirectUrl);
    }

    private static void EnsureReplayMatches(PaymentEntity payment, CreateSubscriptionPaymentCommand request)
    {
        if (payment.ReferenceType != PaymentReferenceType.SUBSCRIPTION
            || payment.ReferenceId != request.UpgradeAttemptId
            || payment.OperatorId != request.OperatorId
            || payment.Amount.Amount != request.Amount)
        {
            throw new CodedConflictException(
                "IDEMPOTENCY_KEY_MISMATCH",
                "Idempotency-Key was already used with a different subscription payment request.");
        }
    }
}
