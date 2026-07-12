using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
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
    private readonly ILogger<SubscriptionLifecycleJob> _logger;

    public SubscriptionLifecycleJob(
        IOperatorSubscriptionRepository subscriptions,
        ISubscriptionUpgradeAttemptRepository attempts,
        ISubscriptionPaymentClient payments,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<SubscriptionLifecycleJob> logger)
    {
        _subscriptions = subscriptions;
        _attempts = attempts;
        _payments = payments;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
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

        var pendingThreshold = now.AddHours(-24);
        var pending = await _attempts.Query()
            .Where(attempt => attempt.Status == SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING
                && attempt.CreatedAt <= pendingThreshold
                && !attempt.WarnSentAt.HasValue)
            .ToListAsync(cancellationToken);
        foreach (var attempt in pending)
        {
            attempt.MarkWarningSent(now);
            _attempts.Update(attempt);
            await EnqueueAsync("identity.subscription.payment_pending_warn", new
            {
                subscriptionId = attempt.SubscriptionId,
                operatorId = attempt.OperatorId,
                paymentId = attempt.PaymentId,
                dueAt = attempt.DueAt,
                occurredAt = now,
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task AutoRevertAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var due = await _attempts.ListDueAsync(SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING, now, cancellationToken);
        foreach (var attempt in due)
        {
            if (!attempt.PaymentId.HasValue)
                continue;

            await _payments.ExpireAsync(attempt.PaymentId.Value, $"subscription-revert:{attempt.Id:N}", cancellationToken);
            var subscription = await _subscriptions.GetByIdAsync(attempt.SubscriptionId, cancellationToken);
            if (subscription is null || subscription.Status != SubscriptionStatus.PENDING_PAYMENT)
                continue;

            var previousPlanId = subscription.PreviousActivePlanId;
            var restoredPlanId = previousPlanId ?? SubscriptionPlan.StarterPlanId;
            subscription.RevertPendingPayment(restoredPlanId, now);
            attempt.MarkExpired(attempt.PaymentId.Value);
            _subscriptions.Update(subscription);
            _attempts.Update(attempt);
            await EnqueueAsync("identity.subscription.payment_auto_reverted", new
            {
                subscriptionId = subscription.Id,
                operatorId = subscription.OperatorId,
                previousPlanId,
                restoredPlanId,
                occurredAt = now,
            }, cancellationToken);
        }

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
}
