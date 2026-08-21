using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Subscriptions.ListSubscriptionPlans;

public sealed class ListSubscriptionPlansQueryHandler
    : IRequestHandler<ListSubscriptionPlansQuery, IReadOnlyList<SubscriptionPlanDto>>
{
    private readonly ISubscriptionPlanRepository _plans;

    public ListSubscriptionPlansQueryHandler(ISubscriptionPlanRepository plans)
    {
        _plans = plans;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> Handle(
        ListSubscriptionPlansQuery request,
        CancellationToken cancellationToken)
    {
        var query = _plans.QueryNoTracking();
        if (!request.IncludeInactive)
            query = query.Where(plan => plan.IsActive);
        if (request.OperatorId.HasValue)
        {
            query = query.Where(plan => plan.PlanType == SubscriptionPlanType.STANDARD
                || plan.OwnerOperatorId == request.OperatorId.Value);
        }

        var plans = await query.OrderBy(plan => plan.PricePerMonth).ThenBy(plan => plan.Name).ToListAsync(cancellationToken);
        return plans.Select(SubscriptionMapper.ToPlanDto).ToArray();
    }
}
