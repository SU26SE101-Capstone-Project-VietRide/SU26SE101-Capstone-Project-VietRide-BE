namespace VietRide.Identity.Application.Features.Admin.CreateOperator;

public sealed record OperatorSubscriptionSummaryDto(
    Guid SubscriptionId,
    Guid PlanId,
    string PlanName,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExpiresAt,
    int CurrentOperatorUsers);
