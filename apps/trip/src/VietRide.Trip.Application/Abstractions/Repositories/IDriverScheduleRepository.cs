using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IDriverScheduleRepository : IRepository<DriverSchedule, Guid>
{
    Task<bool> HasDriverConflictAsync(
        Guid driverUserId,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        Guid? excludeScheduleId = null,
        CancellationToken cancellationToken = default);
}
