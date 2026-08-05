using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
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

    public async Task<IReadOnlyList<DriverSchedule>> ListByRouteIdsAsync(
        Guid operatorId,
        IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.DriverSchedules
            .AsNoTracking()
            .Where(schedule => schedule.OperatorId == operatorId && routeIds.Contains(schedule.RouteId))
            .ToListAsync(cancellationToken);
    }

    public async Task<DriverSchedule?> AcquireOwnedForUpdateAsync(
        Guid scheduleId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for DriverSchedule acquisition.");
        }

        var schedule = await dbContext.DriverSchedules
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_trip.driver_schedules
                WHERE id = {scheduleId} AND operator_id = {operatorId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (schedule is not null)
        {
            await dbContext.Entry(schedule).ReloadAsync(cancellationToken);
        }

        return schedule;
    }

    public async Task AcquireOverlapLocksAsync(
        Guid driverUserId,
        Guid? assistantUserId,
        Guid? vehicleId,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for DriverSchedule overlap locking.");
        }

        _ = validFrom;
        _ = validUntil;
        var resources = new List<string> { $"driver:{driverUserId:D}" };
        if (assistantUserId.HasValue)
        {
            resources.Add($"assistant:{assistantUserId.Value:D}");
        }

        if (vehicleId.HasValue)
        {
            resources.Add($"vehicle:{vehicleId.Value:D}");
        }

        foreach (var day in dayOfWeek.Distinct().Order())
        {
            var slot = $"{day}:{departureTime:HH:mm:ss}";
            resources.Add($"driver-slot:{driverUserId:D}:{slot}");
            if (assistantUserId.HasValue)
            {
                resources.Add($"assistant-slot:{assistantUserId.Value:D}:{slot}");
            }

            if (vehicleId.HasValue)
            {
                resources.Add($"vehicle-slot:{vehicleId.Value:D}:{slot}");
            }
        }

        foreach (var resource in resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var lockKey = CreateAdvisoryLockKey(resource);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})",
                cancellationToken);
        }
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
        Guid? excludeScheduleId = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = await dbContext.DriverSchedules
            .AsNoTracking()
            .Where(schedule =>
                schedule.DriverUserId == driverUserId
                && schedule.IsActive
                && schedule.DepartureTime == departureTime
                && (!excludeScheduleId.HasValue || schedule.Id != excludeScheduleId.Value)
                && (!schedule.ValidUntil.HasValue || schedule.ValidUntil.Value >= validFrom)
                && (!validUntil.HasValue || schedule.ValidFrom <= validUntil.Value))
            .Select(schedule => schedule.DayOfWeek)
            .ToListAsync(cancellationToken);

        return candidates.Any(existingDays =>
            (existingDays.Deserialize<int[]>() ?? []).Intersect(dayOfWeek).Any());
    }

    public Task<bool> HasAssistantConflictAsync(
        Guid assistantUserId,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        Guid? excludeScheduleId = null,
        CancellationToken cancellationToken = default) =>
        HasConflictAsync(
            schedule => schedule.AssistantUserId == assistantUserId,
            dayOfWeek,
            departureTime,
            validFrom,
            validUntil,
            excludeScheduleId,
            cancellationToken);

    public Task<bool> HasVehicleConflictAsync(
        Guid vehicleId,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        Guid? excludeScheduleId = null,
        CancellationToken cancellationToken = default) =>
        HasConflictAsync(
            schedule => schedule.VehicleId == vehicleId,
            dayOfWeek,
            departureTime,
            validFrom,
            validUntil,
            excludeScheduleId,
            cancellationToken);

    private async Task<bool> HasConflictAsync(
        System.Linq.Expressions.Expression<Func<DriverSchedule, bool>> identityPredicate,
        IReadOnlyCollection<int> dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        Guid? excludeScheduleId,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.DriverSchedules
            .AsNoTracking()
            .Where(identityPredicate)
            .Where(schedule =>
                schedule.IsActive
                && schedule.DepartureTime == departureTime
                && (!excludeScheduleId.HasValue || schedule.Id != excludeScheduleId.Value)
                && (!schedule.ValidUntil.HasValue || schedule.ValidUntil.Value >= validFrom)
                && (!validUntil.HasValue || schedule.ValidFrom <= validUntil.Value))
            .Select(schedule => schedule.DayOfWeek)
            .ToListAsync(cancellationToken);

        return candidates.Any(existingDays =>
            (existingDays.Deserialize<int[]>() ?? []).Intersect(dayOfWeek).Any());
    }

    private static long CreateAdvisoryLockKey(string resource)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"driver-schedule:{resource}"));
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
