using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

public sealed class OperatorSubscription : BaseEntity<Guid>
{
    public Guid OperatorId { get; private set; }
    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public SubscriptionPaymentMethod? PaymentMethod { get; private set; }
    public SubscriptionBillingPeriod? BillingPeriod { get; private set; }
    public int CurrentVehicles { get; private set; }
    public int CurrentDrivers { get; private set; }
    public int CurrentAssistants { get; private set; }
    public int CurrentOperatorUsers { get; private set; }
    public int CurrentRoutes { get; private set; }
    public int CurrentTripsThisMonth { get; private set; }
    public DateTimeOffset LastResetAt { get; private set; }
    public DateTimeOffset? TrialExpiringWarnSentAt { get; private set; }

    private OperatorSubscription() { }

    public static OperatorSubscription CreatePendingApproval(Guid operatorId, Guid planId, DateTimeOffset createdAt)
    {
        return new OperatorSubscription
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            PlanId = planId,
            Status = SubscriptionStatus.PENDING_APPROVAL,
            LastResetAt = createdAt,
        };
    }

    public static OperatorSubscription CreateActiveTrial(
        Guid operatorId,
        Guid planId,
        DateTimeOffset startedAt,
        DateTimeOffset expiresAt)
    {
        var subscription = CreatePendingApproval(operatorId, planId, startedAt);
        subscription.ActivateTrial(startedAt, expiresAt);
        return subscription;
    }

    public void ActivateTrial(DateTimeOffset startedAt, DateTimeOffset expiresAt)
    {
        if (Status != SubscriptionStatus.PENDING_APPROVAL)
        {
            throw new InvalidOperationException("Only pending-approval subscriptions can be activated.");
        }

        if (expiresAt <= startedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, "Expiry must be after the start date.");
        }

        Status = SubscriptionStatus.ACTIVE;
        StartedAt = startedAt;
        ExpiresAt = expiresAt;
        PaymentMethod = null;
    }

    public void CancelPendingApproval()
    {
        if (Status != SubscriptionStatus.PENDING_APPROVAL)
        {
            throw new InvalidOperationException("Only pending-approval subscriptions can be cancelled by operator rejection.");
        }

        Status = SubscriptionStatus.CANCELLED;
    }

    public void MoveToPendingPayment(SubscriptionPaymentMethod paymentMethod)
    {
        if (Status == SubscriptionStatus.PENDING_PAYMENT)
        {
            throw new InvalidOperationException("A subscription payment is already pending.");
        }

        if (Status is not (SubscriptionStatus.ACTIVE or SubscriptionStatus.EXPIRED))
        {
            throw new InvalidOperationException("Only active or expired subscriptions can start an upgrade.");
        }

        Status = SubscriptionStatus.PENDING_PAYMENT;
        PaymentMethod = paymentMethod;
    }

    public void ExpirePendingPayment(
        SubscriptionFallbackPolicy fallbackPolicy,
        Guid starterPlanId,
        DateTimeOffset expiredAt)
    {
        if (Status != SubscriptionStatus.PENDING_PAYMENT)
        {
            throw new InvalidOperationException("Only pending-payment subscriptions can be reverted.");
        }

        if (fallbackPolicy == SubscriptionFallbackPolicy.ACTIVATE_STARTER)
        {
            PlanId = starterPlanId;
            StartedAt = expiredAt;
            ExpiresAt = expiredAt.AddDays(30);
            Status = SubscriptionStatus.ACTIVE;
        }
        else
        {
            Status = ExpiresAt.HasValue && ExpiresAt <= expiredAt
                ? SubscriptionStatus.EXPIRED
                : SubscriptionStatus.ACTIVE;
        }

        PaymentMethod = null;
        BillingPeriod = null;
    }

    public void ActivatePaid(
        Guid planId,
        SubscriptionBillingPeriod billingPeriod,
        SubscriptionPaymentMethod paymentMethod,
        DateTimeOffset periodFrom,
        DateTimeOffset periodTo)
    {
        if (Status is not (SubscriptionStatus.PENDING_PAYMENT or SubscriptionStatus.EXPIRED))
        {
            throw new InvalidOperationException("Only pending-payment or expired subscriptions can activate a paid plan.");
        }

        PlanId = planId;
        BillingPeriod = billingPeriod;
        PaymentMethod = paymentMethod;
        Status = SubscriptionStatus.ACTIVE;
        StartedAt = periodFrom;
        ExpiresAt = periodTo;
    }

    public void MarkExpired(DateTimeOffset expiredAt)
    {
        if (Status != SubscriptionStatus.ACTIVE)
        {
            throw new InvalidOperationException("Only active subscriptions can expire.");
        }

        if (ExpiresAt is not null && expiredAt < ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiredAt), expiredAt, "Expiry marker cannot be before the configured expiry.");
        }

        Status = SubscriptionStatus.EXPIRED;
    }

    public void IncrementUsage(SubscriptionUsageResource resource, int amount = 1)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Increment amount must be positive.");
        }

        checked
        {
            switch (resource)
            {
                case SubscriptionUsageResource.VEHICLES:
                    CurrentVehicles += amount;
                    break;
                case SubscriptionUsageResource.DRIVERS:
                    CurrentDrivers += amount;
                    break;
                case SubscriptionUsageResource.ASSISTANTS:
                    CurrentAssistants += amount;
                    break;
                case SubscriptionUsageResource.OPERATOR_USERS:
                    CurrentOperatorUsers += amount;
                    break;
                case SubscriptionUsageResource.ROUTES:
                    CurrentRoutes += amount;
                    break;
                case SubscriptionUsageResource.TRIPS_THIS_MONTH:
                    CurrentTripsThisMonth += amount;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resource), resource, "Unknown usage resource.");
            }
        }
    }

    public void ResetMonthlyTripUsage(DateTimeOffset resetAt)
    {
        CurrentTripsThisMonth = 0;
        LastResetAt = resetAt;
    }

    public void MarkTrialExpiryWarningSent(DateTimeOffset sentAt)
    {
        if (!TrialExpiringWarnSentAt.HasValue)
            TrialExpiringWarnSentAt = sentAt;
    }
}
