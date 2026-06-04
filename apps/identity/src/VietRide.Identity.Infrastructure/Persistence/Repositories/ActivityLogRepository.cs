using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

internal sealed class ActivityLogRepository : IActivityLogRepository
{
    private readonly IdentityDbContext _db;

    public ActivityLogRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task<ActivityLog?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.ActivityLogs.FindAsync([id], ct);

    public async Task<ActivityLog> AddAsync(ActivityLog entity, CancellationToken ct)
    {
        await _db.ActivityLogs.AddAsync(entity, ct);
        return entity;
    }

    public void Update(ActivityLog entity)
        => _db.ActivityLogs.Update(entity);

    public void Remove(ActivityLog entity)
        => _db.ActivityLogs.Remove(entity);

    public IQueryable<ActivityLog> Query()
        => _db.ActivityLogs;

    public IQueryable<ActivityLog> QueryNoTracking()
        => _db.ActivityLogs.AsNoTracking();
}
