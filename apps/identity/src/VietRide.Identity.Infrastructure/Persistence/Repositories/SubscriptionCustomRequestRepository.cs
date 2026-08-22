using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionCustomRequestRepository : ISubscriptionCustomRequestRepository
{
    private readonly IdentityDbContext _dbContext;

    public SubscriptionCustomRequestRepository(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<SubscriptionCustomRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionCustomRequests.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SubscriptionCustomRequest?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionCustomRequests
            .FromSqlInterpolated($"SELECT * FROM vietride_identity.subscription_custom_requests WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

    public Task<SubscriptionCustomRequest?> GetPendingByOperatorIdAsync(Guid operatorId, CancellationToken cancellationToken = default)
        => _dbContext.SubscriptionCustomRequests.FirstOrDefaultAsync(
            x => x.OperatorId == operatorId && x.Status == SubscriptionCustomRequestStatus.PENDING_REVIEW,
            cancellationToken);

    public async Task<IReadOnlyList<SubscriptionCustomRequest>> ListForAdminAsync(
        SubscriptionCustomRequestStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SubscriptionCustomRequests.AsNoTracking();
        if (status.HasValue)
            query = query.Where(request => request.Status == status.Value);

        return await query.OrderByDescending(request => request.CreatedAt).ToListAsync(cancellationToken);
    }

    public Task<SubscriptionCustomRequest> AddAsync(SubscriptionCustomRequest entity, CancellationToken cancellationToken = default)
    {
        _dbContext.SubscriptionCustomRequests.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(SubscriptionCustomRequest entity) => _dbContext.SubscriptionCustomRequests.Update(entity);

    public void Remove(SubscriptionCustomRequest entity) => _dbContext.SubscriptionCustomRequests.Remove(entity);

    public IQueryable<SubscriptionCustomRequest> Query() => _dbContext.SubscriptionCustomRequests;

    public IQueryable<SubscriptionCustomRequest> QueryNoTracking() => _dbContext.SubscriptionCustomRequests.AsNoTracking();
}
