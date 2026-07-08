using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ILocationRepository : IRepository<Location, Guid>
{
    Task<Location?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Location?> GetActiveByCodeAsync(string code, CancellationToken cancellationToken);

    Task<bool> ExistsByCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken cancellationToken);

    Task<PagedResult<Location>> ListAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        CancellationToken cancellationToken);
}
