using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;
using VietRide.Trip.Application.Features.Internal.Reports.PlatformTrips;
using VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;
using VietRide.Trip.Application.Features.OperatorReports;
using VietRide.Trip.Application.Features.Trips.ListOperatorTrips;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Repositories;

public interface ITripRepository : IRepository<Domain.Entities.Trip, Guid>
{
    Task<IReadOnlyList<InternalTripSummaryDto>> ListSummariesByIdsAsync(
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Internal Trip summary batching is not implemented by this repository.");

    Task<PagedResult<OperatorTripListRow>> ListOperatorTripsAsync(
        Guid operatorId,
        int page,
        int pageSize,
        string? routeSearch,
        string? normalizedPlateSearch,
        TripStatus? status,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        bool sortDescending,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator Trip list is not implemented by this repository.");

    IAsyncEnumerable<TripOperatorOccupancyRow> StreamOperatorOccupancyRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Operator occupancy report is not implemented by this repository.");
    Task<IReadOnlyList<PlatformTripReportItem>> GetPlatformTripMetricsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Platform Trip report is not implemented by this repository.");

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

    Task<IReadOnlyList<Domain.Entities.Trip>> ListPendingByDriverScheduleAsync(
        Guid driverScheduleId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("DriverSchedule Trip enumeration is not supported by this repository implementation.");

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

    Task<Domain.Entities.Trip?> GetForUpdateAsync(Guid tripId, CancellationToken cancellationToken)
        => GetByIdAsync(tripId, cancellationToken);

    Task<Domain.Entities.Trip?> GetRouteChangePreflightAsync(
        Guid tripId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Route-change preflight is not supported by this repository implementation.");

    Task<Domain.Entities.Trip?> AcquireForRouteChangeAsync(
        Guid tripId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Route-change locking is not supported by this repository implementation.");

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

    Task<TripCargoTransferRepositoryResult> TransferCargoAsync(
        Guid sourceTripId,
        Guid parcelId,
        Guid targetTripId,
        string targetState,
        bool allowCapacityOverflow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Atomic cargo transfer is not supported by this repository implementation.");
}
