using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionQuotaAllocationRepository : ISubscriptionQuotaAllocationRepository
{
    private readonly IdentityDbContext _dbContext;

    public SubscriptionQuotaAllocationRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<SubscriptionQuotaAllocation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Set<SubscriptionQuotaAllocation>().FirstOrDefaultAsync(allocation => allocation.Id == id, cancellationToken);

    public Task<SubscriptionQuotaAllocation> AddAsync(SubscriptionQuotaAllocation entity, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<SubscriptionQuotaAllocation>().Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(SubscriptionQuotaAllocation entity) => _dbContext.Set<SubscriptionQuotaAllocation>().Update(entity);
    public void Remove(SubscriptionQuotaAllocation entity) => _dbContext.Set<SubscriptionQuotaAllocation>().Remove(entity);
    public IQueryable<SubscriptionQuotaAllocation> Query() => _dbContext.Set<SubscriptionQuotaAllocation>();
    public IQueryable<SubscriptionQuotaAllocation> QueryNoTracking() => _dbContext.Set<SubscriptionQuotaAllocation>().AsNoTracking();

    public Task<SubscriptionQuotaAllocation?> GetActiveAsync(Guid operatorId, SubscriptionUsageResource resource, Guid resourceId, CancellationToken cancellationToken = default)
        => _dbContext.Set<SubscriptionQuotaAllocation>().FirstOrDefaultAsync(
            allocation => allocation.OperatorId == operatorId
                && allocation.Resource == resource
                && allocation.ResourceId == resourceId
                && allocation.ReleasedAt == null,
            cancellationToken);

    public Task AcquireLockAsync(Guid operatorId, SubscriptionUsageResource resource, Guid resourceId, CancellationToken cancellationToken = default)
    {
        var lockKey = $"subscription-quota:{operatorId:N}:{resource}:{resourceId:N}";
        return _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockKey})::bigint)",
            cancellationToken);
    }
}
