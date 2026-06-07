namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

public sealed record InternalSubscriptionPlanDto(
    Guid PlanId,
    string Name,
    InternalSubscriptionLimitsDto Limits,
    InternalSubscriptionModulesDto Modules);
