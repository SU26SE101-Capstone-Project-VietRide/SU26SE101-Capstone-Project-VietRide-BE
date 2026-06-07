namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

public sealed record InternalOperatorSubscriptionDto(
    Guid OperatorId,
    Guid SubscriptionId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExpiresAt,
    InternalSubscriptionPlanDto Plan,
    InternalSubscriptionUsageDto Usage,
    DateTimeOffset LastResetAt);
