using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IDriverScheduleAuditLogRepository
{
    Task AddAsync(DriverScheduleAuditLog auditLog, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DriverScheduleAuditLog>> ListByDriverScheduleIdAsync(
        Guid driverScheduleId,
        CancellationToken cancellationToken = default);
}
