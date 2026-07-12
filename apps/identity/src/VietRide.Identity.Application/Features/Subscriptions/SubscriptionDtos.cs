namespace VietRide.Identity.Application.Features.Subscriptions;

public sealed record SubscriptionPlanDto(
    Guid PlanId,
    string Name,
    string? Description,
    long PricePerMonth,
    long PricePerYear,
    SubscriptionLimitsDto Limits,
    SubscriptionModulesDto Modules,
    bool IsActive);

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
    PendingSubscriptionUpgradeDto? PendingUpgrade);

public sealed record PendingSubscriptionUpgradeDto(
    Guid UpgradeAttemptId,
    Guid TargetPlanId,
    string BillingPeriod,
    long Amount,
    Guid? PaymentId,
    DateTimeOffset DueAt);

public sealed record SubscriptionUpgradeResponseDto(
    Guid SubscriptionId,
    Guid UpgradeAttemptId,
    string Status,
    Guid PaymentId,
    long Amount,
    string BillingPeriod,
    string PaymentRedirectUrl,
    DateTimeOffset DueAt);
