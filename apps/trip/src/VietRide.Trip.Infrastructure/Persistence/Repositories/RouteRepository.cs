using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class RouteRepository : IRouteRepository
{
    private readonly TripDbContext dbContext;

    public RouteRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<Route?> GetByIdAsync(Guid id, CancellationToken ct)
        => dbContext.Routes.FirstOrDefaultAsync(route => route.Id == id, ct);

    public Task<Route> AddAsync(Route entity, CancellationToken ct)
    {
        dbContext.Routes.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(Route entity)
        => dbContext.Routes.Update(entity);

    public void Remove(Route entity)
        => dbContext.Routes.Remove(entity);

    public IQueryable<Route> Query()
        => dbContext.Routes;

    public IQueryable<Route> QueryNoTracking()
        => dbContext.Routes.AsNoTracking();

    public Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
        => dbContext.Routes.FirstOrDefaultAsync(route =>
            route.Id == routeId
            && route.OperatorId == operatorId
            && route.DeletedAt == null,
            cancellationToken);

    public Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
        => dbContext.Routes.FirstOrDefaultAsync(route =>
            route.Id == routeId
            && route.OperatorId == operatorId
            && route.IsActive
            && route.DeletedAt == null,
            cancellationToken);

    public async Task<IReadOnlyList<Route>> ListByOperatorAsync(
        Guid operatorId,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Routes
            .AsNoTracking()
            .Where(route => route.OperatorId == operatorId && route.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var trimmedSearch = search.Trim();
            query = query.Where(route => route.Name.Contains(trimmedSearch));
        }

        return await query
            .OrderBy(route => route.Name)
            .ThenBy(route => route.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
        => dbContext.Routes.AnyAsync(route =>
            route.Id == routeId
            && route.OperatorId == operatorId
            && route.IsActive
            && route.DeletedAt == null,
            cancellationToken);

    public async Task<Route?> FindDuplicateWithTransactionLockAsync(
        Guid operatorId,
        string name,
        Guid originStationId,
        Guid destinationStationId,
        Guid? excludedRouteId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A transaction is required for duplicate Route detection.");

        var normalizedName = name.Trim().ToLowerInvariant();
        var lockKey = $"{operatorId:D}|{originStationId:D}|{destinationStationId:D}|{normalizedName}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);

        return await dbContext.Routes
            .AsNoTracking()
            .Where(route => route.OperatorId == operatorId
                && route.DeletedAt == null
                && route.OriginStationId == originStationId
                && route.DestinationStationId == destinationStationId
                && (!excludedRouteId.HasValue || route.Id != excludedRouteId.Value)
                && route.Name.Trim().ToLower() == normalizedName)
            .OrderBy(route => route.CreatedAt)
            .ThenBy(route => route.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> HasStationMergeConflictAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
        => dbContext.Routes
            .IgnoreQueryFilters()
            .AnyAsync(route =>
                (route.OriginStationId == duplicateStationId && route.DestinationStationId == primaryStationId)
                || (route.OriginStationId == primaryStationId && route.DestinationStationId == duplicateStationId),
                cancellationToken);

    public async Task<(int OriginCount, int DestinationCount)> RelinkForStationMergeAsync(
        Guid duplicateStationId,
        Guid primaryStationId,
        CancellationToken cancellationToken = default)
    {
        var routes = await dbContext.Routes
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.routes WHERE origin_station_id = {duplicateStationId} OR destination_station_id = {duplicateStationId} ORDER BY id::text FOR UPDATE")
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
        var originCount = 0;
        var destinationCount = 0;
        foreach (var route in routes)
        {
            var relink = route.RelinkStation(duplicateStationId, primaryStationId);
            if (relink.OriginChanged)
                originCount++;
            if (relink.DestinationChanged)
                destinationCount++;
        }

        return (originCount, destinationCount);
    }

    public async Task<Route?> AcquireOwnedActiveAsync(
        Guid operatorId,
        Guid routeId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for Route acquisition.");
        }

        var route = await dbContext.Routes
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_trip.routes
                WHERE id = {routeId}
                    AND operator_id = {operatorId}
                    AND is_active = TRUE
                    AND deleted_at IS NULL
                FOR SHARE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (route is null)
        {
            return null;
        }

        await dbContext.Entry(route).ReloadAsync(cancellationToken);
        return route.OperatorId == operatorId
            && route.IsActive
            && route.DeletedAt is null
            ? route
            : null;
    }
}
