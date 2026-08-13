using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ILocationRepository : IRepository<Location, Guid>
{
    Task<Location?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Location?> GetActiveByCodeAsync(string code, CancellationToken cancellationToken);

    Task<bool> ExistsByCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Location>> ListActiveTopLevelAsync(string? search, CancellationToken cancellationToken)
    {
        var normalizedSearch = search?.Trim();
        IReadOnlyList<Location> result = QueryNoTracking()
            .Where(location => location.IsActive && location.ParentLocationId == null)
            .AsEnumerable()
            .Where(location => string.IsNullOrWhiteSpace(normalizedSearch)
                || location.Code.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                || location.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(location => location.SortOrder)
            .ThenBy(location => location.Name)
            .ToList();
        return Task.FromResult(result);
    }

    Task<IReadOnlyList<Location>> ListActiveChildrenAsync(Guid parentId, string? search, CancellationToken cancellationToken)
    {
        var normalizedSearch = search?.Trim();
        IReadOnlyList<Location> result = QueryNoTracking()
            .Where(location => location.IsActive && location.ParentLocationId == parentId)
            .AsEnumerable()
            .Where(location => string.IsNullOrWhiteSpace(normalizedSearch)
                || location.Code.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                || location.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(location => location.SortOrder)
            .ThenBy(location => location.Name)
            .ToList();
        return Task.FromResult(result);
    }

    Task<PagedResult<Location>> ListAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken);

    Task<PagedResult<Location>> ListAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken,
        string? type = null,
        Guid? parentLocationId = null) =>
        ListAsync(page, pageSize, search, isActive, cancellationToken);
}
