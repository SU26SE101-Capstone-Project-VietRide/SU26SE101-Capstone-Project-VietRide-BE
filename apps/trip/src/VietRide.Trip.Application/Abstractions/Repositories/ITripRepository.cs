using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripRepository : IRepository<Domain.Entities.Trip, Guid>
{
    Task<Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken);

    Task<TripCargoMutationResult?> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Cargo counters are not supported by this repository implementation.");

    Task<TripCargoMutationResult?> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
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
