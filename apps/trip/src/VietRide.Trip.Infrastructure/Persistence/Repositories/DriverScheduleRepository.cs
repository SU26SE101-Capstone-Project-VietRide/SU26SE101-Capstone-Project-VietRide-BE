using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

public sealed class DriverScheduleRepository : IDriverScheduleRepository
{
    private readonly TripDbContext dbContext;

    public DriverScheduleRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<DriverSchedule?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.DriverSchedules.FindAsync([id], cancellationToken);
    }

    public async Task<DriverSchedule> AddAsync(
        DriverSchedule entity,
        CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.DriverSchedules.AddAsync(entity, cancellationToken);
        return entry.Entity;
    }

    public void Update(DriverSchedule entity)
    {
        dbContext.DriverSchedules.Update(entity);
    }

    public void Remove(DriverSchedule entity)
    {
        dbContext.DriverSchedules.Remove(entity);
    }

    public IQueryable<DriverSchedule> Query()
    {
        return dbContext.DriverSchedules;
    }

    public IQueryable<DriverSchedule> QueryNoTracking()
    {
        return dbContext.DriverSchedules.AsNoTracking();
    }

    public async Task<bool> HasDriverConflictAsync(
        Guid driverUserId,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        CancellationToken cancellationToken = default)
    {
        var candidates = await dbContext.DriverSchedules
            .AsNoTracking()
            .Where(schedule =>
                schedule.DriverUserId == driverUserId
                && schedule.IsActive
                && schedule.DepartureTime == departureTime
                && (!schedule.ValidUntil.HasValue || schedule.ValidUntil.Value >= validFrom)
                && (!validUntil.HasValue || schedule.ValidFrom <= validUntil.Value))
            .Select(schedule => schedule.DayOfWeek)
            .ToListAsync(cancellationToken);

        return candidates.Any(existingDays =>
            (existingDays.Deserialize<int[]>() ?? []).Intersect(dayOfWeek).Any());
    }
}
