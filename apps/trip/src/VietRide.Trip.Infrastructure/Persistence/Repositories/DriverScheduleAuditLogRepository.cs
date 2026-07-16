using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

public sealed class DriverScheduleAuditLogRepository : IDriverScheduleAuditLogRepository
{
    private readonly TripDbContext _dbContext;

    public DriverScheduleAuditLogRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        DriverScheduleAuditLog auditLog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditLog);
        await _dbContext.DriverScheduleAuditLogs.AddAsync(auditLog, cancellationToken);
    }

    public async Task<IReadOnlyList<DriverScheduleAuditLog>> ListByDriverScheduleIdAsync(
        Guid driverScheduleId,
        CancellationToken cancellationToken = default)
    {
        if (driverScheduleId == Guid.Empty)
        {
            throw new ArgumentException("Driver schedule id cannot be empty.", nameof(driverScheduleId));
        }

        return await _dbContext.DriverScheduleAuditLogs
            .AsNoTracking()
            .Where(auditLog => auditLog.DriverScheduleId == driverScheduleId)
            .OrderByDescending(auditLog => auditLog.OccurredAt)
            .ToListAsync(cancellationToken);
    }
}
