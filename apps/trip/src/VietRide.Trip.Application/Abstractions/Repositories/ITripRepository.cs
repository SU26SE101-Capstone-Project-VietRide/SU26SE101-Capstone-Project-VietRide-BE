using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripRepository : IRepository<Domain.Entities.Trip, Guid>
{
    Task<Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken);

    Task<Domain.Entities.Trip?> GetForUpdateAsync(Guid tripId, CancellationToken cancellationToken)
        => GetByIdAsync(tripId, cancellationToken);

    Task<DriverTripRouteDto?> GetDriverTripRouteAsync(Guid tripId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Driver trip route reads are not supported by this repository implementation.");

    Task<TripCargoMutationResult?> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Cargo counters are not supported by this repository implementation.");

    Task<TripCargoMutationResult?> RemeasureReservedCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Cargo counters are not supported by this repository implementation.");

    Task<TripCargoMutationResult?> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Cargo counters are not supported by this repository implementation.");

    Task<TripCargoMutationResult?> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Cargo counters are not supported by this repository implementation.");
}
