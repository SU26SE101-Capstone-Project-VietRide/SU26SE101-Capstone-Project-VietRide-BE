using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Domain.Entities;

public sealed class SubscriptionUpgradeAttempt : BaseEntity<Guid>
{
    public Guid SubscriptionId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid TargetPlanId { get; private set; }
    public SubscriptionBillingPeriod BillingPeriod { get; private set; }
    public Money Amount { get; private set; } = Money.Zero;
    public SubscriptionPaymentMethod PaymentMethod { get; private set; }
    public SubscriptionUpgradeAttemptStatus Status { get; private set; }
    public Guid? PaymentId { get; private set; }
    public SubscriptionPaymentSessionStatus LatestPaymentStatus { get; private set; }
    public int PaymentSessionVersion { get; private set; }
    public SubscriptionFallbackPolicy FallbackPolicy { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset DueAt { get; private set; }

    private SubscriptionUpgradeAttempt() { }

    public static SubscriptionUpgradeAttempt Create(
        Guid subscriptionId,
        Guid operatorId,
        Guid targetPlanId,
        SubscriptionBillingPeriod billingPeriod,
        Money amount,
        SubscriptionPaymentMethod paymentMethod,
        string idempotencyKey,
        SubscriptionFallbackPolicy fallbackPolicy,
        DateTimeOffset createdAt,
        DateTimeOffset dueAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (dueAt <= createdAt)
            throw new ArgumentOutOfRangeException(nameof(dueAt), "Due time must be after creation time.");

        return new SubscriptionUpgradeAttempt
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            OperatorId = operatorId,
            TargetPlanId = targetPlanId,
            BillingPeriod = billingPeriod,
            Amount = amount,
            PaymentMethod = paymentMethod,
            Status = SubscriptionUpgradeAttemptStatus.INITIATED,
            LatestPaymentStatus = SubscriptionPaymentSessionStatus.NONE,
            FallbackPolicy = fallbackPolicy,
            IdempotencyKey = idempotencyKey.Trim(),
            DueAt = dueAt,
        };
    }

    public static SubscriptionUpgradeAttempt Create(
        Guid subscriptionId,
        Guid operatorId,
        Guid targetPlanId,
        SubscriptionBillingPeriod billingPeriod,
        Money amount,
        SubscriptionPaymentMethod paymentMethod,
        string idempotencyKey,
        DateTimeOffset createdAt,
        DateTimeOffset dueAt)
        => Create(
            subscriptionId,
            operatorId,
            targetPlanId,
            billingPeriod,
            amount,
            paymentMethod,
            idempotencyKey,
            SubscriptionFallbackPolicy.RESTORE_CURRENT,
            createdAt,
            dueAt);

    public void BindPendingPayment(Guid paymentId)
    {
        if (Status is not (SubscriptionUpgradeAttemptStatus.INITIATED or SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING))
            throw new InvalidOperationException("Only an active upgrade attempt can bind a pending payment.");

        if (LatestPaymentStatus == SubscriptionPaymentSessionStatus.PENDING && PaymentId != paymentId)
            throw new InvalidOperationException("A payment session is already pending for this upgrade attempt.");

        if (PaymentId != paymentId)
            PaymentSessionVersion++;
        PaymentId = paymentId;
        LatestPaymentStatus = SubscriptionPaymentSessionStatus.PENDING;
        Status = SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING;
    }

    public void MarkPaymentFailed(Guid paymentId)
    {
        EnsureLatestPayment(paymentId);
        LatestPaymentStatus = SubscriptionPaymentSessionStatus.FAILED;
    }

    public void MarkPaymentExpired(Guid paymentId)
    {
        EnsureLatestPayment(paymentId);
        LatestPaymentStatus = SubscriptionPaymentSessionStatus.EXPIRED;
    }

    public void MarkSucceeded(Guid paymentId)
    {
        if (Status == SubscriptionUpgradeAttemptStatus.SUCCEEDED && PaymentId == paymentId)
            return;
        if (Status != SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING || PaymentId != paymentId)
            throw new InvalidOperationException("Only the pending bound payment can complete an upgrade attempt.");

        Status = SubscriptionUpgradeAttemptStatus.SUCCEEDED;
        LatestPaymentStatus = SubscriptionPaymentSessionStatus.SUCCEEDED;
    }

    public void MarkExpired(Guid paymentId)
    {
        if (Status == SubscriptionUpgradeAttemptStatus.EXPIRED && PaymentId == paymentId)
            return;
        if (Status != SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING || PaymentId != paymentId)
            throw new InvalidOperationException("Only the pending bound payment can expire an upgrade attempt.");

        Status = SubscriptionUpgradeAttemptStatus.EXPIRED;
        LatestPaymentStatus = SubscriptionPaymentSessionStatus.EXPIRED;
    }

    public void MarkExpired()
    {
        if (Status == SubscriptionUpgradeAttemptStatus.EXPIRED)
            return;
        if (Status is not (SubscriptionUpgradeAttemptStatus.INITIATED or SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING))
            throw new InvalidOperationException("Only an active upgrade attempt can expire.");

        Status = SubscriptionUpgradeAttemptStatus.EXPIRED;
        if (LatestPaymentStatus == SubscriptionPaymentSessionStatus.PENDING)
            LatestPaymentStatus = SubscriptionPaymentSessionStatus.EXPIRED;
    }

    public void MarkFailed()
    {
        if (Status == SubscriptionUpgradeAttemptStatus.FAILED)
            return;
        if (Status is not (SubscriptionUpgradeAttemptStatus.INITIATED or SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING))
            throw new InvalidOperationException("Only an active upgrade attempt can fail.");

        Status = SubscriptionUpgradeAttemptStatus.FAILED;
        LatestPaymentStatus = SubscriptionPaymentSessionStatus.FAILED;
    }

    private void EnsureLatestPayment(Guid paymentId)
    {
        if (Status != SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING || PaymentId != paymentId)
            throw new InvalidOperationException("Only the latest payment session can change this upgrade attempt.");
    }
}
