namespace VietRide.Payment.Application.Features.Internal.Payments.GetSubscriptionPaymentStatuses;

public sealed record SubscriptionPaymentStatusDto(
    Guid PaymentId,
    Guid UpgradeAttemptId,
    Guid OperatorId,
    Guid OperatorSubscriptionId,
    Guid PlanId,
    string Status,
    long Amount,
    string Method,
    string BillingPeriod,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    DateTimeOffset? SucceededAt,
    DateTimeOffset? DueAt);
