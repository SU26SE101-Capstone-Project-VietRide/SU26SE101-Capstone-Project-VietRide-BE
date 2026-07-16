using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface IVehicleRepository : IRepository<Vehicle, Guid>
{
    Task<Vehicle?> GetOwnedByIdAsync(Guid operatorId, Guid vehicleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Vehicle>> AcquireForVehicleSwapAsync(
        Guid operatorId,
        IReadOnlyCollection<Guid> vehicleIds,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Vehicle-swap locking is not supported by this repository implementation.");

    Task<PagedResult<Vehicle>> ListByOperatorAsync(
        Guid operatorId,
        int page,
        int pageSize,
        string? search,
        string? searchIn,
        string? sortBy,
        string sortDir,
        CancellationToken cancellationToken);

    Task<bool> LicensePlateExistsAsync(
        string licensePlate,
        Guid? excludedVehicleId,
        CancellationToken cancellationToken);

    Task<bool> TryAddAsync(Vehicle vehicle, CancellationToken cancellationToken);

    Task<bool> TryUpdateAsync(Vehicle vehicle, CancellationToken cancellationToken);
}
