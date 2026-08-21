using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionPlanRepository : ISubscriptionPlanRepository
{
    private readonly IdentityDbContext _dbContext;

    public SubscriptionPlanRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<SubscriptionPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionPlans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SubscriptionPlan?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionPlans
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.subscription_plans WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

    public Task<SubscriptionPlan> AddAsync(SubscriptionPlan entity, CancellationToken cancellationToken = default)
    {
        _dbContext.SubscriptionPlans.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(SubscriptionPlan entity)
        => _dbContext.SubscriptionPlans.Update(entity);

    public void Remove(SubscriptionPlan entity)
        => _dbContext.SubscriptionPlans.Remove(entity);

    public IQueryable<SubscriptionPlan> Query()
        => _dbContext.SubscriptionPlans;

    public IQueryable<SubscriptionPlan> QueryNoTracking()
        => _dbContext.SubscriptionPlans.AsNoTracking();

    public Task<SubscriptionPlan?> GetStarterPlanAsync(CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionPlans.FirstOrDefaultAsync(
            x => x.Id == SubscriptionPlan.StarterPlanId && x.IsActive,
            cancellationToken);
}
