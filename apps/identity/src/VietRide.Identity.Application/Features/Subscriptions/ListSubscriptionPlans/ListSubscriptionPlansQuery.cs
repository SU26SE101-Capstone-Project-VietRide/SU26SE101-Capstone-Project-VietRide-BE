using MediatR;

namespace VietRide.Identity.Application.Features.Subscriptions.ListSubscriptionPlans;

public sealed record ListSubscriptionPlansQuery(bool IncludeInactive) : IRequest<IReadOnlyList<SubscriptionPlanDto>>;
