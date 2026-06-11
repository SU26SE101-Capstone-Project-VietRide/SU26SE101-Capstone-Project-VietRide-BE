using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class VehicleTypeRepository : IVehicleTypeRepository
{
    private readonly TripDbContext dbContext;

    public VehicleTypeRepository(TripDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<VehicleType?> GetByIdAsync(Guid id, CancellationToken ct)
        => dbContext.VehicleTypes.FirstOrDefaultAsync(vehicleType => vehicleType.Id == id, ct);

    public Task<VehicleType> AddAsync(VehicleType entity, CancellationToken ct)
    {
        dbContext.VehicleTypes.Add(entity);
        return Task.FromResult(entity);
    }

    public void Update(VehicleType entity)
        => dbContext.VehicleTypes.Update(entity);

    public void Remove(VehicleType entity)
        => dbContext.VehicleTypes.Remove(entity);

    public IQueryable<VehicleType> Query()
        => dbContext.VehicleTypes;

    public IQueryable<VehicleType> QueryNoTracking()
        => dbContext.VehicleTypes.AsNoTracking();

    public Task<VehicleType?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken)
        => dbContext.VehicleTypes.FirstOrDefaultAsync(
            vehicleType => vehicleType.Id == id && vehicleType.IsActive,
            cancellationToken);

    public async Task<PagedResult<VehicleType>> ListActiveAsync(
        int page,
        int pageSize,
        string? search,
        string? searchIn,
        string? sortBy,
        string sortDir,
        CancellationToken cancellationToken)
    {
        var query = dbContext.VehicleTypes
            .AsNoTracking()
            .Where(vehicleType => vehicleType.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            var searchFields = ParseSearchFields(searchIn);
            var searchCode = searchFields.Contains("code");
            var searchDisplayName = searchFields.Contains("displayName");
            query = query.Where(vehicleType =>
                (searchCode && EF.Functions.ILike(vehicleType.Code, pattern))
                || (searchDisplayName && EF.Functions.ILike(vehicleType.DisplayName, pattern)));
        }

        var totalItems = await query.LongCountAsync(cancellationToken);
        var items = await ApplySort(query, sortBy, sortDir)
            .ThenBy(vehicleType => vehicleType.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<VehicleType>.Create(items, page, pageSize, totalItems);
    }

    private static HashSet<string> ParseSearchFields(string? searchIn)
    {
        if (string.IsNullOrWhiteSpace(searchIn))
            return new HashSet<string>(["code", "displayName"], StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            searchIn.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IOrderedQueryable<VehicleType> ApplySort(
        IQueryable<VehicleType> query,
        string? sortBy,
        string sortDir)
    {
        var descending = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        return sortBy?.Trim().ToLowerInvariant() switch
        {
            "code" => descending ? query.OrderByDescending(x => x.Code) : query.OrderBy(x => x.Code),
            "defaultseatcount" => descending ? query.OrderByDescending(x => x.DefaultSeatCount) : query.OrderBy(x => x.DefaultSeatCount),
            "issystemdefined" => descending ? query.OrderByDescending(x => x.IsSystemDefined) : query.OrderBy(x => x.IsSystemDefined),
            "createdat" => descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            "updatedat" => descending ? query.OrderByDescending(x => x.UpdatedAt) : query.OrderBy(x => x.UpdatedAt),
            _ => descending ? query.OrderByDescending(x => x.DisplayName) : query.OrderBy(x => x.DisplayName),
        };
    }
}
