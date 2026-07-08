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

    public async Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken cancellationToken)
        => await dbContext.Locations
            .AsNoTracking()
            .Where(location => location.IsActive)
            .OrderBy(location => location.SortOrder)
            .ThenBy(location => location.Name)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<Location>> ListAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Locations.AsNoTracking();

        if (isActive.HasValue)
        {
            query = query.Where(location => location.IsActive == isActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(location =>
                EF.Functions.ILike(location.Code, pattern)
                || EF.Functions.ILike(location.Name, pattern));
        }

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
