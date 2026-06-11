using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface ISubscriptionPlanRepository : IRepository<SubscriptionPlan, Guid>
{
    Task<SubscriptionPlan?> GetStarterPlanAsync(CancellationToken cancellationToken = default);
}
