namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed record AdminSubscriptionCustomRequestDto(
    Guid RequestId,
    Guid OperatorId,
    string OperatorName,
    SubscriptionLimitsDto RequestedLimits,
    SubscriptionModulesDto RequestedModules,
    string PreferredBillingPeriod,
    string? Note,
    string Status,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? RejectionReason,
    Guid? ApprovedPlanId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
