using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IDriverScheduleRepository : IRepository<DriverSchedule, Guid>
{
    Task<IReadOnlyList<DriverSchedule>> ListByRouteIdsAsync(
        Guid operatorId,
        IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DriverSchedule>>(
            QueryNoTracking()
                .Where(schedule => schedule.OperatorId == operatorId && routeIds.Contains(schedule.RouteId))
                .ToList());

    Task<DriverSchedule?> AcquireOwnedForUpdateAsync(
        Guid scheduleId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("DriverSchedule locking is not supported by this repository implementation.");

    Task AcquireOverlapLocksAsync(
        Guid driverUserId,
        Guid? assistantUserId,
        Guid? vehicleId,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("DriverSchedule overlap locking is not supported by this repository implementation.");

    Task<bool> HasDriverConflictAsync(
        Guid driverUserId,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        Guid? excludeScheduleId = null,
        CancellationToken cancellationToken = default);

    Task<bool> HasAssistantConflictAsync(
        Guid assistantUserId,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        Guid? excludeScheduleId = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Assistant conflict checks are not supported by this repository implementation.");

    Task<bool> HasVehicleConflictAsync(
        Guid vehicleId,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        Guid? excludeScheduleId = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Vehicle schedule conflict checks are not supported by this repository implementation.");
}
