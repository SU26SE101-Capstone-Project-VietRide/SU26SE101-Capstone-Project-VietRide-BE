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
    public SubscriptionUpgradeAttemptStatus Status { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset? WarnSentAt { get; private set; }

    private SubscriptionUpgradeAttempt() { }

    public static SubscriptionUpgradeAttempt Create(
        Guid subscriptionId,
        Guid operatorId,
        Guid targetPlanId,
        SubscriptionBillingPeriod billingPeriod,
        Money amount,
        string idempotencyKey,
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
            Status = SubscriptionUpgradeAttemptStatus.INITIATED,
            IdempotencyKey = idempotencyKey.Trim(),
            DueAt = dueAt,
        };
    }

    public void BindPendingPayment(Guid paymentId)
    {
        if (Status is not (SubscriptionUpgradeAttemptStatus.INITIATED or SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING))
            throw new InvalidOperationException("Only an initiated upgrade attempt can bind a pending payment.");

        if (PaymentId.HasValue && PaymentId != paymentId)
            throw new InvalidOperationException("A different payment is already bound to this upgrade attempt.");

        PaymentId = paymentId;
        Status = SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING;
    }

    public void MarkSucceeded(Guid paymentId)
    {
        if (Status == SubscriptionUpgradeAttemptStatus.SUCCEEDED && PaymentId == paymentId)
            return;
        if (Status != SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING || PaymentId != paymentId)
            throw new InvalidOperationException("Only the pending bound payment can complete an upgrade attempt.");

        Status = SubscriptionUpgradeAttemptStatus.SUCCEEDED;
    }

    public void MarkExpired(Guid paymentId)
    {
        if (Status == SubscriptionUpgradeAttemptStatus.EXPIRED && PaymentId == paymentId)
            return;
        if (Status != SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING || PaymentId != paymentId)
            throw new InvalidOperationException("Only the pending bound payment can expire an upgrade attempt.");

        Status = SubscriptionUpgradeAttemptStatus.EXPIRED;
    }

    public void MarkWarningSent(DateTimeOffset sentAt)
    {
        if (WarnSentAt.HasValue)
            return;

        WarnSentAt = sentAt;
    }
}
