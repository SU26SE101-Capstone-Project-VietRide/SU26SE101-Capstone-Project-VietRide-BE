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

    public async Task<bool> ActivateAsync(
        SubscriptionPaymentActivationContext context,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var subscription = await _subscriptions.GetByIdForUpdateAsync(
                context.OperatorSubscriptionId,
                cancellationToken);
            var attempt = await _attempts.GetByIdForUpdateAsync(context.UpgradeAttemptId, cancellationToken);

            if (attempt is null
                || subscription is null
                || attempt.OperatorId != context.OperatorId
                || attempt.SubscriptionId != context.OperatorSubscriptionId
                || attempt.TargetPlanId != context.TargetPlanId
                || attempt.Amount.Amount != context.Amount
                || !string.Equals(attempt.BillingPeriod.ToString(), context.BillingPeriod, StringComparison.Ordinal)
                || !Enum.TryParse<SubscriptionPaymentMethod>(context.Method, false, out var paymentMethod)
                || context.PeriodTo != (attempt.BillingPeriod == SubscriptionBillingPeriod.MONTHLY
                    ? context.PeriodFrom.AddMonths(1)
                    : context.PeriodFrom.AddYears(1)))
            {
                await QuarantineAsync(context, "context mismatch", cancellationToken);
                return false;
            }

            if (attempt.Status == SubscriptionUpgradeAttemptStatus.SUCCEEDED)
            {
                await _unitOfWork.CommitAsync(cancellationToken);
                return true;
            }

            if (context.SucceededAt >= attempt.DueAt
                || attempt.Status is SubscriptionUpgradeAttemptStatus.EXPIRED or SubscriptionUpgradeAttemptStatus.FAILED)
            {
                await QuarantineAsync(context, "payment succeeded after attempt deadline or terminal state", cancellationToken);
                return false;
            }

            if (attempt.Status is not (SubscriptionUpgradeAttemptStatus.INITIATED
                or SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING))
            {
                await QuarantineAsync(context, $"attempt status {attempt.Status}", cancellationToken);
                return false;
            }

            if (attempt.PaymentId.HasValue && attempt.PaymentId != context.PaymentId
                && attempt.LatestPaymentStatus == SubscriptionPaymentSessionStatus.PENDING)
            {
                await QuarantineAsync(context, "another payment session is pending", cancellationToken);
                return false;
            }

            attempt.BindPendingPayment(context.PaymentId);
            if (subscription.Status is SubscriptionStatus.ACTIVE or SubscriptionStatus.EXPIRED)
                subscription.MoveToPendingPayment(paymentMethod);
            else if (subscription.Status != SubscriptionStatus.PENDING_PAYMENT)
            {
                await QuarantineAsync(context, $"subscription status {subscription.Status}", cancellationToken);
                return false;
            }

            subscription.ActivatePaid(
                attempt.TargetPlanId,
                attempt.BillingPeriod,
                paymentMethod,
                context.PeriodFrom,
                context.PeriodTo);
            attempt.MarkSucceeded(context.PaymentId);
            _subscriptions.Update(subscription);
            _attempts.Update(attempt);
            await _unitOfWork.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task QuarantineAsync(
        SubscriptionPaymentActivationContext context,
        string reason,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Quarantining subscription activation for event {EventId}, payment {PaymentId}, attempt {UpgradeAttemptId}: {Reason}.",
            context.EventId,
            context.PaymentId,
            context.UpgradeAttemptId,
            reason);
        await _unitOfWork.RollbackAsync(cancellationToken);
    }
}
