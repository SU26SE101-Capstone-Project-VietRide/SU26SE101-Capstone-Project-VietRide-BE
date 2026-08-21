using MediatR;

namespace VietRide.Identity.Application.Features.Subscriptions.ListSubscriptionPlans;

public sealed record ListSubscriptionPlansQuery(
    bool IncludeInactive,
    Guid? OperatorId = null) : IRequest<IReadOnlyList<SubscriptionPlanDto>>;
