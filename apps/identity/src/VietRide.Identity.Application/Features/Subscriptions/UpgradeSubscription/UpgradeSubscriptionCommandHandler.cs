using MediatR;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Subscriptions.UpgradeSubscription;

public sealed class UpgradeSubscriptionCommandHandler
    : IRequestHandler<UpgradeSubscriptionCommand, SubscriptionUpgradeResponseDto>
{
    private static readonly TimeSpan PaymentWindow = TimeSpan.FromDays(7);

    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly ISubscriptionPaymentClient _payments;
    private readonly IClock _clock;

    public UpgradeSubscriptionCommandHandler(
        IOperatorSubscriptionRepository subscriptions,
        ISubscriptionPlanRepository plans,
        ISubscriptionUpgradeAttemptRepository attempts,
        ISubscriptionPaymentClient payments,
        IClock clock)
    {
        _subscriptions = subscriptions;
        _plans = plans;
        _attempts = attempts;
        _payments = payments;
        _clock = clock;
    }

    public async Task<SubscriptionUpgradeResponseDto> Handle(
        UpgradeSubscriptionCommand request,
        CancellationToken cancellationToken)
    {
        var replay = await _attempts.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplayMatches(replay, request);
            if (!replay.PaymentId.HasValue)
                throw new CodedConflictException("SUBSCRIPTION_PAYMENT_PENDING", "Subscription payment is still being initialized.");

            var paymentReplay = await _payments.CreateAsync(
                new SubscriptionPaymentCreationRequest(
                    replay.Id,
                    replay.SubscriptionId,
                    replay.OperatorId,
                    replay.TargetPlanId,
                    replay.BillingPeriod.ToString(),
                    replay.Amount.Amount,
                    request.IdempotencyKey,
                    request.ClientIpAddress),
                cancellationToken);

            return new SubscriptionUpgradeResponseDto(
                replay.SubscriptionId,
                replay.Id,
                SubscriptionStatus.PENDING_PAYMENT.ToString(),
                paymentReplay.PaymentId,
                replay.Amount.Amount,
                replay.BillingPeriod.ToString(),
                paymentReplay.PaymentRedirectUrl,
                replay.DueAt);
        }

        var targetPlan = await _plans.GetByIdAsync(request.PlanId, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionPlan), request.PlanId);
        if (!targetPlan.IsActive)
            throw new CodedValidationException("SUBSCRIPTION_PLAN_INACTIVE", "The selected subscription plan is inactive.");

        var subscription = await _subscriptions.GetCurrentByOperatorIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(OperatorSubscription), request.OperatorId);
        if (subscription.Status == SubscriptionStatus.PENDING_PAYMENT)
            throw new CodedConflictException("SUBSCRIPTION_PAYMENT_PENDING", "An upgrade payment is already pending.");

        var billingPeriod = SubscriptionMapper.ParseBillingPeriod(request.BillingPeriod);
        var amount = billingPeriod == SubscriptionBillingPeriod.MONTHLY
            ? targetPlan.PricePerMonth
            : targetPlan.PricePerYear;
        if (amount.Amount <= 0)
            throw new CodedValidationException("SUBSCRIPTION_PLAN_NOT_PAYABLE", "The selected plan has no payable VNPay price.");

        var now = _clock.UtcNow;
        subscription.MoveToPendingPayment(targetPlan.Id, SubscriptionPaymentMethod.VNPAY);
        _subscriptions.Update(subscription);
        var attempt = SubscriptionUpgradeAttempt.Create(
            subscription.Id,
            request.OperatorId,
            targetPlan.Id,
            billingPeriod,
            amount,
            request.IdempotencyKey,
            now,
            now.Add(PaymentWindow));
        await _attempts.AddAsync(attempt, cancellationToken);

        var payment = await _payments.CreateAsync(
            new SubscriptionPaymentCreationRequest(
                attempt.Id,
                subscription.Id,
                request.OperatorId,
                targetPlan.Id,
                billingPeriod.ToString(),
                amount.Amount,
                request.IdempotencyKey,
                request.ClientIpAddress),
            cancellationToken);
        attempt.BindPendingPayment(payment.PaymentId);

        return new SubscriptionUpgradeResponseDto(
            subscription.Id,
            attempt.Id,
            subscription.Status.ToString(),
            payment.PaymentId,
            amount.Amount,
            billingPeriod.ToString(),
            payment.PaymentRedirectUrl,
            attempt.DueAt);
    }

    private static void EnsureReplayMatches(SubscriptionUpgradeAttempt attempt, UpgradeSubscriptionCommand request)
    {
        if (attempt.OperatorId != request.OperatorId
            || attempt.TargetPlanId != request.PlanId
            || !string.Equals(attempt.BillingPeriod.ToString(), request.BillingPeriod, StringComparison.Ordinal))
        {
            throw new CodedConflictException(
                "IDEMPOTENCY_KEY_MISMATCH",
                "Idempotency-Key was already used with a different subscription upgrade request.");
        }
    }
}
