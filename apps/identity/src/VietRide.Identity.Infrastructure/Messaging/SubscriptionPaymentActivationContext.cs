namespace VietRide.Identity.Infrastructure.Messaging;

public sealed record SubscriptionPaymentActivationContext(
    Guid EventId,
    Guid PaymentId,
    Guid UpgradeAttemptId,
    Guid OperatorId,
    Guid OperatorSubscriptionId,
    Guid TargetPlanId,
    long Amount,
    string Method,
    string BillingPeriod,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    DateTimeOffset SucceededAt);
