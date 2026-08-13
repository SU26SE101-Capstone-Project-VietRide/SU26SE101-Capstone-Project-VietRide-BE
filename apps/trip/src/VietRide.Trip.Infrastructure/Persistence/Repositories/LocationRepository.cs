using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class LocationRepository : ILocationRepository
{
    private readonly TripDbContext dbContext;

    public LocationRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<Location?> GetByIdAsync(Guid id, CancellationToken ct)
        => dbContext.Locations.FirstOrDefaultAsync(location => location.Id == id, ct);

    public Task<Location> AddAsync(Location entity, CancellationToken ct)
    {
        dbContext.Locations.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(Location entity)
        => dbContext.Locations.Update(entity);

    public void Remove(Location entity)
        => dbContext.Locations.Remove(entity);

    public IQueryable<Location> Query()
        => dbContext.Locations;

    public IQueryable<Location> QueryNoTracking()
        => dbContext.Locations.AsNoTracking();

    public Task<Location?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.Locations.FirstOrDefaultAsync(
            location => location.Id == id && location.IsActive,
            cancellationToken);

    public Task<Location?> GetActiveByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return dbContext.Locations.FirstOrDefaultAsync(
            location => location.Code == normalized && location.IsActive,
            cancellationToken);
    }

    public Task<bool> ExistsByCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return dbContext.Locations.AnyAsync(
            location => location.Code == normalized && (!exceptId.HasValue || location.Id != exceptId.Value),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Location>> ListActiveTopLevelAsync(string? search, CancellationToken cancellationToken)
    {
        var query = BuildAccentInsensitiveSearchQuery(search)
            .Where(location => location.IsActive && location.ParentLocationId == null);

        return await query
            .OrderBy(location => location.SortOrder)
            .ThenBy(location => location.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Location>> ListActiveChildrenAsync(
        Guid parentId,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = BuildAccentInsensitiveSearchQuery(search)
            .Where(location => location.IsActive && location.ParentLocationId == parentId);

        return await query
            .OrderBy(location => location.SortOrder)
            .ThenBy(location => location.Name)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Location> BuildAccentInsensitiveSearchQuery(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return dbContext.Locations.AsNoTracking();
        }

        var normalized = search.Trim();
        return dbContext.Locations
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_trip.locations
                WHERE unaccent(code) ILIKE unaccent('%' || {normalized} || '%')
                   OR unaccent(name) ILIKE unaccent('%' || {normalized} || '%')
                """)
            .AsNoTracking();
    }

    public async Task<PagedResult<Location>> ListAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken)
        => await ListAsync(page, pageSize, search, isActive, cancellationToken, null, null);

    public async Task<PagedResult<Location>> ListAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken,
        string? type = null,
        Guid? parentLocationId = null)
    {
        var query = BuildAccentInsensitiveSearchQuery(search);

        if (isActive.HasValue)
        {
            query = query.Where(location => location.IsActive == isActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(location => location.Type == type);
        if (parentLocationId.HasValue)
            query = query.Where(location => location.ParentLocationId == parentLocationId.Value);

        var totalItems = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(location => location.SortOrder)
            .ThenBy(location => location.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<Location>.Create(items, page, pageSize, totalItems);
    }
}
