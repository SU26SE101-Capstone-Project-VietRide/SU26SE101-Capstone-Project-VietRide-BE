using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;

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

        var plans = await query.OrderBy(plan => plan.PricePerMonth).ThenBy(plan => plan.Name).ToListAsync(cancellationToken);
        return plans.Select(SubscriptionMapper.ToPlanDto).ToArray();
    }
}
