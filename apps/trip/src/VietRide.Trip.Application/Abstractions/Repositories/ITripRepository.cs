using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripRepository : IRepository<Domain.Entities.Trip, Guid>
{
    Task<IReadOnlyList<Guid>> ListScheduledForAutoBoardingAsync(
        DateTimeOffset latestDeparture,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Lifecycle candidate scans are not supported by this repository implementation.");

    Task<IReadOnlyList<Guid>> ListBoardingForAutoStartAsync(
        DateTimeOffset departureBefore,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Lifecycle candidate scans are not supported by this repository implementation.");

    Task<IReadOnlyList<Guid>> ListInProgressForAutoCompletionAsync(
        DateTimeOffset arrivalBefore,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Lifecycle candidate scans are not supported by this repository implementation.");

    Task<Domain.Entities.Trip?> AcquireForLifecycleTransitionAsync(
        Guid tripId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Lifecycle transitions are not supported by this repository implementation.");

    Task<Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken);

    Task<Domain.Entities.Trip?> AcquireForVehicleSwapAsync(
        Guid tripId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Vehicle-swap locking is not supported by this repository implementation.");

    Task<bool> HasVehicleConflictAsync(
        Guid vehicleId,
        DateTimeOffset departureDateTime,
        Guid excludedTripId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Vehicle conflict checks are not supported by this repository implementation.");

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
