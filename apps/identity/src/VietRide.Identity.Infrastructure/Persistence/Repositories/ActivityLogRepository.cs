using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Infrastructure.Persistence.Repositories;

internal sealed class ActivityLogRepository : IActivityLogRepository
{
    private readonly IdentityDbContext _db;

    public ActivityLogRepository(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task<ActivityLog?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.ActivityLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(activityLog => activityLog.Actor)
            .SingleOrDefaultAsync(activityLog => activityLog.Id == id, ct);

    public async Task<ActivityLog> AddAsync(ActivityLog entity, CancellationToken ct)
    {
        await _db.ActivityLogs.AddAsync(entity, ct);
        return entity;
    }

    public Task<bool> ExistsBySourceEventIdAsync(Guid sourceEventId, CancellationToken ct = default)
        => _db.ActivityLogs
            .AsNoTracking()
            .AnyAsync(activityLog => activityLog.SourceEventId == sourceEventId, ct);

    public async Task<PagedResult<ActivityLog>> ListAsync(
        QueryOptions options,
        Guid? actorUserId,
        ActivityLogAction? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var query = _db.ActivityLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(activityLog => activityLog.Actor)
            .AsQueryable();

        if (actorUserId.HasValue)
            query = query.Where(activityLog => activityLog.UserId == actorUserId.Value);

        if (action.HasValue)
            query = query.Where(activityLog => activityLog.Action == action.Value);

        if (from.HasValue)
            query = query.Where(activityLog => activityLog.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(activityLog => activityLog.CreatedAt < to.Value);

        var totalItems = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(activityLog => activityLog.CreatedAt)
            .ThenByDescending(activityLog => activityLog.Id)
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToListAsync(ct);

        return PagedResult<ActivityLog>.Create(
            items,
            options.Page,
            options.PageSize,
            totalItems);
    }
}
