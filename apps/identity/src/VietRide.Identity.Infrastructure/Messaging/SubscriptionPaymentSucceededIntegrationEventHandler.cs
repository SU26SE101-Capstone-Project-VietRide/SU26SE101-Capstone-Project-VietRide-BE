using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionPaymentSucceededIntegrationEventHandler
    : IIntegrationEventHandler<SubscriptionPaymentSucceededIntegrationEvent>
{
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubscriptionPaymentSucceededIntegrationEventHandler> _logger;

    public SubscriptionPaymentSucceededIntegrationEventHandler(
        ISubscriptionUpgradeAttemptRepository attempts,
        IOperatorSubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        ILogger<SubscriptionPaymentSucceededIntegrationEventHandler> logger)
    {
        _attempts = attempts;
        _subscriptions = subscriptions;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        SubscriptionPaymentSucceededIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Keep the lock order aligned with the upgrade command to avoid a consumer/API deadlock.
            var subscription = await _subscriptions.GetByIdForUpdateAsync(
                integrationEvent.OperatorSubscriptionId,
                cancellationToken);
            var attempt = await _attempts.GetByIdForUpdateAsync(
                integrationEvent.UpgradeAttemptId,
                cancellationToken);

            if (attempt is null
                || subscription is null
                || attempt.OperatorId != integrationEvent.OperatorId
                || (attempt.PaymentId.HasValue && attempt.PaymentId != integrationEvent.PaymentId)
                || attempt.Amount.Amount != integrationEvent.Amount
                || attempt.SubscriptionId != integrationEvent.OperatorSubscriptionId
                || !string.Equals(attempt.BillingPeriod.ToString(), integrationEvent.BillingPeriod, StringComparison.Ordinal)
                || !Enum.TryParse<SubscriptionPaymentMethod>(integrationEvent.Method, false, out var paymentMethod)
                || integrationEvent.PeriodTo != (attempt.BillingPeriod == SubscriptionBillingPeriod.MONTHLY
                    ? integrationEvent.PeriodFrom.AddMonths(1)
                    : integrationEvent.PeriodFrom.AddYears(1)))
            {
                _logger.LogWarning(
                    "Ignoring invalid subscription payment event {EventId} for upgrade attempt {UpgradeAttemptId}.",
                    integrationEvent.EventId,
                    integrationEvent.UpgradeAttemptId);
                await _unitOfWork.RollbackAsync(cancellationToken);
                return;
            }

            if (attempt.Status == SubscriptionUpgradeAttemptStatus.SUCCEEDED)
            {
                await _unitOfWork.CommitAsync(cancellationToken);
                return;
            }

            if (attempt.Status is not (SubscriptionUpgradeAttemptStatus.INITIATED
                or SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING))
            {
                _logger.LogWarning(
                    "Ignoring subscription payment event {EventId}; upgrade attempt {UpgradeAttemptId} is {Status}.",
                    integrationEvent.EventId,
                    attempt.Id,
                    attempt.Status);
                await _unitOfWork.RollbackAsync(cancellationToken);
                return;
            }

            attempt.BindPendingPayment(integrationEvent.PaymentId);
            if (subscription.Status is SubscriptionStatus.ACTIVE or SubscriptionStatus.EXPIRED)
            {
                subscription.MoveToPendingPayment(attempt.TargetPlanId, paymentMethod);
            }
            else if (subscription.Status != SubscriptionStatus.PENDING_PAYMENT
                || subscription.PlanId != attempt.TargetPlanId
                || subscription.PaymentMethod != paymentMethod)
            {
                _logger.LogWarning(
                    "Ignoring subscription payment event {EventId}; subscription {SubscriptionId} has incompatible state {Status}.",
                    integrationEvent.EventId,
                    subscription.Id,
                    subscription.Status);
                await _unitOfWork.RollbackAsync(cancellationToken);
                return;
            }

            subscription.ActivatePaid(
                attempt.TargetPlanId,
                attempt.BillingPeriod,
                paymentMethod,
                integrationEvent.PeriodFrom,
                integrationEvent.PeriodTo);
            attempt.MarkSucceeded(integrationEvent.PaymentId);
            _subscriptions.Update(subscription);
            _attempts.Update(attempt);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
