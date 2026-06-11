using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IVehicleTypeRepository : IRepository<VehicleType, Guid>
{
    Task<VehicleType?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<VehicleType>> ListActiveAsync(
        int page,
        int pageSize,
        string? search,
        string? searchIn,
        string? sortBy,
        string sortDir,
        CancellationToken cancellationToken);
}
