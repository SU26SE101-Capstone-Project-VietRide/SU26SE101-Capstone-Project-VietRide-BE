using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

public sealed class OperatorSubscriptionRepository : IOperatorSubscriptionRepository
{
    private readonly IdentityDbContext _dbContext;

    public OperatorSubscriptionRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<OperatorSubscription?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.OperatorSubscriptions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<OperatorSubscription> AddAsync(
        OperatorSubscription entity,
        CancellationToken cancellationToken = default)
    {
        _dbContext.OperatorSubscriptions.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(OperatorSubscription entity)
        => _dbContext.OperatorSubscriptions.Update(entity);

    public void Remove(OperatorSubscription entity)
        => _dbContext.OperatorSubscriptions.Remove(entity);

    public IQueryable<OperatorSubscription> Query()
        => _dbContext.OperatorSubscriptions;

    public IQueryable<OperatorSubscription> QueryNoTracking()
        => _dbContext.OperatorSubscriptions.AsNoTracking();

    public Task<OperatorSubscription?> GetCurrentByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.OperatorSubscriptions
            .Where(x => x.OperatorId == operatorId)
            .Where(x => x.Status == SubscriptionStatus.PENDING_APPROVAL || x.Status == SubscriptionStatus.ACTIVE)
            .OrderByDescending(x => x.StartedAt ?? x.LastResetAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
