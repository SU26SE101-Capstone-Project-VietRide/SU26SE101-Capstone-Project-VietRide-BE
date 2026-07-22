using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionUpgradeAttemptRepository : ISubscriptionUpgradeAttemptRepository
{
    private readonly IdentityDbContext _dbContext;

    public SubscriptionUpgradeAttemptRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<SubscriptionUpgradeAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionUpgradeAttempts.FirstOrDefaultAsync(attempt => attempt.Id == id, cancellationToken);

    public Task<SubscriptionUpgradeAttempt> AddAsync(SubscriptionUpgradeAttempt entity, CancellationToken cancellationToken = default)
    {
        _dbContext.SubscriptionUpgradeAttempts.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(SubscriptionUpgradeAttempt entity) => _dbContext.SubscriptionUpgradeAttempts.Update(entity);

    public void Remove(SubscriptionUpgradeAttempt entity) => _dbContext.SubscriptionUpgradeAttempts.Remove(entity);

    public IQueryable<SubscriptionUpgradeAttempt> Query() => _dbContext.SubscriptionUpgradeAttempts;

    public IQueryable<SubscriptionUpgradeAttempt> QueryNoTracking() => _dbContext.SubscriptionUpgradeAttempts.AsNoTracking();

    public Task<SubscriptionUpgradeAttempt?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionUpgradeAttempts.FirstOrDefaultAsync(attempt => attempt.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<SubscriptionUpgradeAttempt?> GetPendingBySubscriptionIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionUpgradeAttempts
            .Where(attempt => attempt.SubscriptionId == subscriptionId && attempt.Status == SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING)
            .OrderByDescending(attempt => attempt.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<SubscriptionUpgradeAttempt?> GetActiveBySubscriptionIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionUpgradeAttempts
            .Where(attempt => attempt.SubscriptionId == subscriptionId
                && (attempt.Status == SubscriptionUpgradeAttemptStatus.INITIATED
                    || attempt.Status == SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING))
            .OrderByDescending(attempt => attempt.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<SubscriptionUpgradeAttempt?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionUpgradeAttempts
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.subscription_upgrade_attempts WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionUpgradeAttempt>> ListDueAsync(SubscriptionUpgradeAttemptStatus status, DateTimeOffset dueBefore, CancellationToken cancellationToken = default)
        => await _dbContext.SubscriptionUpgradeAttempts
            .Where(attempt => attempt.Status == status && attempt.DueAt <= dueBefore)
            .OrderBy(attempt => attempt.DueAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionUpgradeAttempt>> ListActiveAsync(
        int take,
        CancellationToken cancellationToken = default)
        => await _dbContext.SubscriptionUpgradeAttempts
            .Where(attempt => attempt.Status == SubscriptionUpgradeAttemptStatus.INITIATED
                || attempt.Status == SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING)
            .OrderBy(attempt => attempt.DueAt)
            .Take(take)
            .ToListAsync(cancellationToken);
}
