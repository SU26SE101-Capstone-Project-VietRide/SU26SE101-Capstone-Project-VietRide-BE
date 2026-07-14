using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

public sealed class TripAuditLogRepository : ITripAuditLogRepository
{
    private readonly TripDbContext _dbContext;

    public TripAuditLogRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditLog);
        await _dbContext.TripAuditLogs.AddAsync(auditLog, cancellationToken);
    }

    public async Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty)
        {
            throw new ArgumentException("Trip id cannot be empty.", nameof(tripId));
        }

        return await _dbContext.TripAuditLogs
            .AsNoTracking()
            .Where(auditLog => auditLog.TripId == tripId)
            .OrderByDescending(auditLog => auditLog.OccurredAt)
            .ToListAsync(cancellationToken);
    }
}
