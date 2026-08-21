namespace VietRide.Identity.Application.Features.Subscriptions;

public sealed record SubscriptionPlanDto(
    Guid PlanId,
    string Name,
    string? Description,
    long PricePerMonth,
    long PricePerYear,
    SubscriptionLimitsDto Limits,
    SubscriptionModulesDto Modules,
    bool IsActive,
    string PlanType = "STANDARD",
    Guid? OwnerOperatorId = null);

public sealed record SubscriptionLimitsDto(
    int MaxVehicles,
    int MaxDrivers,
    int MaxAssistants,
    int MaxOperatorUsers,
    int MaxRoutes,
    int MaxTripsPerMonth);

public sealed record SubscriptionModulesDto(bool EnableParcel, bool EnableShuttle, bool EnableRag);

public sealed record SubscriptionUsageDto(
    int CurrentVehicles,
    int CurrentDrivers,
    int CurrentAssistants,
    int CurrentOperatorUsers,
    int CurrentRoutes,
    int CurrentTripsThisMonth,
    DateTimeOffset LastResetAt);

public sealed record OperatorSubscriptionDto(
    Guid SubscriptionId,
    string Status,
    string? BillingPeriod,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExpiresAt,
    SubscriptionPlanDto Plan,
    SubscriptionUsageDto Usage,
    PendingSubscriptionUpgradeDto? PendingUpgrade,
    bool EntitlementActive = false);

public sealed record PendingSubscriptionUpgradeDto(
    Guid UpgradeAttemptId,
    SubscriptionPlanDto TargetPlan,
    string BillingPeriod,
    long Amount,
    DateTimeOffset DueAt,
    int RemainingSeconds,
    PendingSubscriptionPaymentDto LatestPayment);

public sealed record PendingSubscriptionPaymentDto(
    Guid? PaymentId,
    string Status,
    bool CanRetry);

public sealed record SubscriptionUpgradeResponseDto(
    Guid SubscriptionId,
    Guid UpgradeAttemptId,
    string Status,
    Guid PaymentId,
    long Amount,
    string BillingPeriod,
    string? PaymentRedirectUrl,
    DateTimeOffset? DueAt,
    string? InvoiceStatus,
    SubscriptionPlanDto? ActivePlan = null,
    SubscriptionPlanDto? PendingTargetPlan = null);

public sealed record SubscriptionUpgradeQuoteDto(
    Guid UpgradeAttemptId,
    Guid SourcePlanId,
    Guid TargetPlanId,
    string BillingPeriod,
    string PaymentMethod,
    bool ProrationApplied,
    long CurrentCyclePrice,
    long TargetCyclePrice,
    long UnusedCredit,
    long ProratedTargetAmount,
    long AmountDue,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    DateTimeOffset QuotedAt,
    DateTimeOffset DueAt,
    string Currency,
    string Status);
