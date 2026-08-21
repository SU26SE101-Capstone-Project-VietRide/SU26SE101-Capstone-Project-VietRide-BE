using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionPaymentActivationService
{
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubscriptionPaymentActivationService> _logger;

    public SubscriptionPaymentActivationService(
        ISubscriptionUpgradeAttemptRepository attempts,
        IOperatorSubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        ILogger<SubscriptionPaymentActivationService> logger)
    {
        _attempts = attempts;
        _subscriptions = subscriptions;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task<bool> ActivateAsync(
        SubscriptionPaymentActivationContext context,
        CancellationToken cancellationToken)
        => _unitOfWork.ExecuteInTransactionAsync(
            () => ActivateWithinTransactionAsync(context, cancellationToken),
            cancellationToken);

    private async Task<bool> ActivateWithinTransactionAsync(
        SubscriptionPaymentActivationContext context,
        CancellationToken cancellationToken)
    {
        var attempt = await _attempts.GetByIdForUpdateAsync(context.UpgradeAttemptId, cancellationToken);
        var subscription = await _subscriptions.GetByIdForUpdateAsync(
            context.OperatorSubscriptionId,
            cancellationToken);

        if (attempt is null
            || subscription is null
            || attempt.OperatorId != context.OperatorId
            || attempt.SubscriptionId != context.OperatorSubscriptionId
            || attempt.TargetPlanId != context.TargetPlanId
            || attempt.Amount.Amount != context.Amount
            || !string.Equals(attempt.BillingPeriod.ToString(), context.BillingPeriod, StringComparison.Ordinal)
            || !Enum.TryParse<SubscriptionPaymentMethod>(context.Method, false, out var paymentMethod)
            || context.PeriodFrom != attempt.PeriodFrom
            || context.PeriodTo != attempt.PeriodTo)
        {
            return Quarantine(context, "context mismatch");
        }

        if (attempt.Status == SubscriptionUpgradeAttemptStatus.SUCCEEDED)
        {
            return attempt.PaymentId == context.PaymentId
                || Quarantine(context, "succeeded attempt is bound to another payment");
        }

        if (context.SucceededAt >= attempt.DueAt
            || attempt.Status is SubscriptionUpgradeAttemptStatus.EXPIRED or SubscriptionUpgradeAttemptStatus.FAILED)
        {
            return Quarantine(context, "payment succeeded after attempt deadline or terminal state");
        }

        if (attempt.Status is not (SubscriptionUpgradeAttemptStatus.INITIATED
            or SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING))
        {
            return Quarantine(context, $"attempt status {attempt.Status}");
        }

        if (attempt.PaymentId.HasValue && attempt.PaymentId != context.PaymentId
            && attempt.LatestPaymentStatus == SubscriptionPaymentSessionStatus.PENDING)
        {
            return Quarantine(context, "another payment session is pending");
        }

        if (subscription.Status is not (SubscriptionStatus.ACTIVE
            or SubscriptionStatus.EXPIRED
            or SubscriptionStatus.PENDING_PAYMENT))
        {
            return Quarantine(context, $"subscription status {subscription.Status}");
        }

        attempt.BindPendingPayment(context.PaymentId);
        if (subscription.Status is SubscriptionStatus.ACTIVE or SubscriptionStatus.EXPIRED)
            subscription.MoveToPendingPayment(paymentMethod);

        subscription.ActivatePaid(
            attempt.TargetPlanId,
            attempt.BillingPeriod,
            paymentMethod,
            context.PeriodFrom,
            context.PeriodTo,
            attempt.TargetCyclePrice,
            attempt.IsProrated);
        attempt.MarkSucceeded(context.PaymentId);
        _subscriptions.Update(subscription);
        _attempts.Update(attempt);
        return true;
    }

    private bool Quarantine(
        SubscriptionPaymentActivationContext context,
        string reason)
    {
        _logger.LogWarning(
            "Quarantining subscription activation for event {EventId}, payment {PaymentId}, attempt {UpgradeAttemptId}: {Reason}.",
            context.EventId,
            context.PaymentId,
            context.UpgradeAttemptId,
            reason);
        return false;
    }
}
