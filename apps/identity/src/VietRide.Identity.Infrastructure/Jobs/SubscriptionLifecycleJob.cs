using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.Messaging;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Infrastructure.Jobs;

public sealed class SubscriptionLifecycleJob
{
    public const string ExpiryJobId = "identity.subscription-expiry";
    public const string WarningJobId = "identity.subscription-warnings";
    public const string RevertJobId = "identity.subscription-auto-revert";
    public const string MonthlyResetJobId = "identity.subscription-monthly-reset";

    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly ISubscriptionPaymentClient _payments;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly SubscriptionPaymentActivationService _activation;
    private readonly ILogger<SubscriptionLifecycleJob> _logger;

    public SubscriptionLifecycleJob(
        IOperatorSubscriptionRepository subscriptions,
        ISubscriptionUpgradeAttemptRepository attempts,
        ISubscriptionPaymentClient payments,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        SubscriptionPaymentActivationService activation,
        ILogger<SubscriptionLifecycleJob> logger)
    {
        _subscriptions = subscriptions;
        _attempts = attempts;
        _payments = payments;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _activation = activation;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task ExpireActiveAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var due = await _subscriptions.Query()
            .Where(subscription => subscription.Status == SubscriptionStatus.ACTIVE
                && subscription.ExpiresAt.HasValue
                && subscription.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var subscription in due)
        {
            subscription.MarkExpired(now);
            _subscriptions.Update(subscription);
            await EnqueueAsync("identity.subscription.expired", new
            {
                subscriptionId = subscription.Id,
                operatorId = subscription.OperatorId,
                expiredAt = now,
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Subscription expiry job expired {Count} subscription(s).", due.Count);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task SendWarningsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var trialThreshold = now.AddDays(3);
        var expiring = await _subscriptions.Query()
            .Where(subscription => subscription.Status == SubscriptionStatus.ACTIVE
                && subscription.ExpiresAt.HasValue
                && subscription.ExpiresAt <= trialThreshold
                && !subscription.TrialExpiringWarnSentAt.HasValue)
            .ToListAsync(cancellationToken);
        foreach (var subscription in expiring)
        {
            subscription.MarkTrialExpiryWarningSent(now);
            _subscriptions.Update(subscription);
            await EnqueueAsync("identity.subscription.trial_expiring", new
            {
                subscriptionId = subscription.Id,
                operatorId = subscription.OperatorId,
                expiresAt = subscription.ExpiresAt,
                daysRemaining = Math.Max(0, (int)Math.Ceiling((subscription.ExpiresAt!.Value - now).TotalDays)),
                occurredAt = now,
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task AutoRevertAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var active = await _attempts.ListActiveAsync(100, cancellationToken);
        if (active.Count == 0)
        {
            await RepairOrphanedPendingSubscriptionsAsync(now, cancellationToken);
            return;
        }

        IReadOnlyList<SubscriptionPaymentStatusResult> paymentStatuses;
        try
        {
            paymentStatuses = await _payments.GetStatusesAsync(
                active.Select(attempt => attempt.Id).ToArray(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Subscription payment reconciliation could not query Payment service.");
            throw;
        }

        var byAttempt = paymentStatuses.ToDictionary(status => status.UpgradeAttemptId);
        foreach (var attempt in active)
        {
            byAttempt.TryGetValue(attempt.Id, out var payment);
            if (payment?.Status == "SUCCEEDED" && payment.SucceededAt.HasValue)
            {
                var activated = await _activation.ActivateAsync(
                    new SubscriptionPaymentActivationContext(
                        Guid.Empty,
                        payment.PaymentId,
                        payment.UpgradeAttemptId,
                        payment.OperatorId,
                        payment.OperatorSubscriptionId,
                        payment.PlanId,
                        payment.Amount,
                        payment.Method,
                        payment.BillingPeriod,
                        payment.PeriodFrom,
                        payment.PeriodTo,
                        payment.SucceededAt.Value),
                    cancellationToken);
                if (activated || attempt.DueAt > now)
                    continue;
            }

            if (attempt.PaymentId.HasValue && payment is null)
                _logger.LogWarning(
                    "Quarantining subscription payment reconciliation for attempt {UpgradeAttemptId}: latest payment {PaymentId} is missing from Payment service.",
                    attempt.Id,
                    attempt.PaymentId);

            if (payment is not null && attempt.PaymentId == payment.PaymentId)
            {
                if (payment.Status == "FAILED" && attempt.LatestPaymentStatus == SubscriptionPaymentSessionStatus.PENDING)
                    attempt.MarkPaymentFailed(payment.PaymentId);
                else if (payment.Status == "EXPIRED" && attempt.LatestPaymentStatus == SubscriptionPaymentSessionStatus.PENDING)
                    attempt.MarkPaymentExpired(payment.PaymentId);
            }

            if (attempt.DueAt > now)
                continue;

            if (payment?.Status == "PENDING_REDIRECT")
                await _payments.ExpireAsync(
                    payment.PaymentId,
                    payment.PaymentId.ToString("D"),
                    cancellationToken);

            var subscription = await _subscriptions.GetByIdAsync(attempt.SubscriptionId, cancellationToken);
            if (subscription?.Status == SubscriptionStatus.PENDING_PAYMENT)
            {
                subscription.ExpirePendingPayment(attempt.FallbackPolicy, SubscriptionPlan.StarterPlanId, now);
                _subscriptions.Update(subscription);
            }
            else if (subscription is null)
            {
                _logger.LogWarning(
                    "Quarantining expired subscription upgrade attempt {UpgradeAttemptId}: subscription {SubscriptionId} is missing.",
                    attempt.Id,
                    attempt.SubscriptionId);
            }

            attempt.MarkExpired();
            _attempts.Update(attempt);
            await EnqueueAsync("identity.subscription.payment_auto_reverted", new
            {
                subscriptionId = attempt.SubscriptionId,
                operatorId = attempt.OperatorId,
                activePlanId = subscription?.PlanId,
                occurredAt = now,
            }, cancellationToken);
        }

        await RepairOrphanedPendingSubscriptionsAsync(now, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task ResetMonthlyTripUsageAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var ictNow = now.ToOffset(TimeSpan.FromHours(7));
        var subscriptions = await _subscriptions.Query()
            .Where(subscription => subscription.LastResetAt < new DateTimeOffset(ictNow.Year, ictNow.Month, 1, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime())
            .ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            subscription.ResetMonthlyTripUsage(now);
            _subscriptions.Update(subscription);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Task EnqueueAsync(string eventType, object payload, CancellationToken cancellationToken)
        => _outbox.EnqueueAsync(eventType, JsonSerializer.Serialize(payload), cancellationToken);

    private async Task RepairOrphanedPendingSubscriptionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var orphaned = await _subscriptions.Query()
            .Where(subscription => subscription.Status == SubscriptionStatus.PENDING_PAYMENT
                && !_attempts.Query().Any(attempt => attempt.SubscriptionId == subscription.Id
                    && (attempt.Status == SubscriptionUpgradeAttemptStatus.INITIATED
                        || attempt.Status == SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING)))
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var subscription in orphaned)
        {
            _logger.LogWarning(
                "Quarantining and repairing pending subscription {SubscriptionId}: no active upgrade attempt exists.",
                subscription.Id);
            subscription.ExpirePendingPayment(
                SubscriptionFallbackPolicy.RESTORE_CURRENT,
                SubscriptionPlan.StarterPlanId,
                now);
            _subscriptions.Update(subscription);
        }

        if (orphaned.Count > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
