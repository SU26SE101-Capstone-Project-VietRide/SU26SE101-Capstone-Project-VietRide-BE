using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;
using VietRide.Trip.Application.Features.Internal.Reports.PlatformTrips;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripRepository : ITripRepository
{
    private readonly TripDbContext _dbContext;

    public TripRepository(TripDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Domain.Entities.Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Trips.FindAsync(new object[] { id }, cancellationToken).AsTask();

    public async Task<IReadOnlyList<PlatformTripReportItem>> GetPlatformTripMetricsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT operator_id,
                   COUNT(*) AS completed_trip_count
            FROM vietride_trip.trips
            WHERE status = 'COMPLETED'::vietride_trip.trip_status
              AND completed_at >= @from_utc
              AND completed_at < @to_utc
            GROUP BY operator_id
            ORDER BY operator_id;
            """;
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        AddParameter(command, "from_utc", fromUtc.ToUniversalTime());
        AddParameter(command, "to_utc", toUtc.ToUniversalTime());

        var items = new List<PlatformTripReportItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new PlatformTripReportItem(reader.GetGuid(0), reader.GetInt64(1)));
        }

        return items;
    }

    public async Task<IReadOnlyList<Guid>> ListScheduledForAutoBoardingAsync(
        DateTimeOffset latestDeparture,
        CancellationToken cancellationToken) =>
        await _dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.Status == TripStatus.SCHEDULED
                && trip.DepartureDateTime <= latestDeparture)
            .Select(trip => trip.Id)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListBoardingForAutoStartAsync(
        DateTimeOffset departureBefore,
        CancellationToken cancellationToken) =>
        await _dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.Status == TripStatus.BOARDING
                && trip.DepartureDateTime < departureBefore)
            .Select(trip => trip.Id)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListInProgressForAutoCompletionAsync(
        DateTimeOffset arrivalBefore,
        CancellationToken cancellationToken) =>
        await _dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.Status == TripStatus.IN_PROGRESS
                && trip.EstimatedArrivalTime < arrivalBefore)
            .Select(trip => trip.Id)
            .ToArrayAsync(cancellationToken);

    public async Task<Domain.Entities.Trip?> AcquireForLifecycleTransitionAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for lifecycle acquisition.");
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM vietride_trip.trips WHERE id = {tripId} FOR UPDATE",
            cancellationToken);

        var trip = await _dbContext.Trips
            .SingleOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);
        if (trip is not null)
        {
            await _dbContext.Entry(trip).ReloadAsync(cancellationToken);
        }

        return trip;
    }

    public async Task<Domain.Entities.Trip> AddAsync(Domain.Entities.Trip entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.Trips.AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(Domain.Entities.Trip entity) => _dbContext.Trips.Update(entity);

    public void Remove(Domain.Entities.Trip entity) => _dbContext.Trips.Remove(entity);

    public IQueryable<Domain.Entities.Trip> Query() => _dbContext.Trips;

    public IQueryable<Domain.Entities.Trip> QueryNoTracking() => _dbContext.Trips.AsNoTracking();

    public Task<Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
        _dbContext.Trips
            .Include(trip => trip.Seats)
            .FirstOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);

    public async Task<IReadOnlyList<Domain.Entities.Trip>> ListPendingByDriverScheduleAsync(
        Guid driverScheduleId,
        CancellationToken cancellationToken) =>
        await _dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.DriverScheduleId == driverScheduleId
                && (trip.Status == TripStatus.SCHEDULED || trip.Status == TripStatus.BOARDING))
            .OrderBy(trip => trip.DepartureDateTime)
            .ThenBy(trip => trip.Id)
            .ToArrayAsync(cancellationToken);

    public async Task<Domain.Entities.Trip?> AcquireForVehicleSwapAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        EnsureCallerTransaction("vehicle-swap Trip acquisition");

        var trip = await _dbContext.Trips
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_trip.trips
                WHERE id = {tripId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (trip is not null)
        {
            await _dbContext.Entry(trip).ReloadAsync(cancellationToken);
        }

        return trip;
    }

    public async Task<bool> HasVehicleConflictAsync(
        Guid vehicleId,
        DateTimeOffset departureDateTime,
        Guid excludedTripId,
        CancellationToken cancellationToken)
    {
        var normalizedDeparture = departureDateTime.ToUniversalTime();
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            var lockKey = CreateVehicleDepartureLockKey(vehicleId, normalizedDeparture);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})",
                cancellationToken);
        }

        return await _dbContext.Trips.AnyAsync(
            trip => trip.VehicleId == vehicleId
                && trip.DepartureDateTime == normalizedDeparture
                && trip.Id != excludedTripId
                && trip.Status != TripStatus.CANCELLED
                && trip.Status != TripStatus.COMPLETED,
            cancellationToken);
    }

    public async Task<Domain.Entities.Trip?> GetForUpdateAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM vietride_trip.trips WHERE id = {tripId} FOR UPDATE",
            cancellationToken);
        return await _dbContext.Trips.FirstOrDefaultAsync(
            trip => trip.Id == tripId,
            cancellationToken);
    }

    public async Task<DriverTripRouteDto?> GetDriverTripRouteAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var routeProjection = await _dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.Id == tripId)
            .Join(
                _dbContext.Routes.AsNoTracking(),
                trip => trip.RouteId,
                route => route.Id,
                (trip, route) => new
                {
                    TripId = trip.Id,
                    RouteId = route.Id,
                    route.PathPolyline,
                    route.OriginStationId,
                    route.DestinationStationId,
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (routeProjection is null)
        {
            return null;
        }

        var stationIds = new[]
        {
            routeProjection.OriginStationId,
            routeProjection.DestinationStationId,
        };
        var stations = await _dbContext.Stations
            .AsNoTracking()
            .Where(station => stationIds.Contains(station.Id))
            .ToDictionaryAsync(station => station.Id, cancellationToken);
        if (!stations.TryGetValue(routeProjection.OriginStationId, out var origin)
            || !stations.TryGetValue(routeProjection.DestinationStationId, out var destination))
        {
            return null;
        }

        var tripStops = await _dbContext.TripStops
            .AsNoTracking()
            .Where(tripStop => tripStop.TripId == tripId)
            .OrderBy(tripStop => tripStop.OrderIndex)
            .ThenBy(tripStop => tripStop.StopId)
            .ToArrayAsync(cancellationToken);
        var stopIds = tripStops.Select(tripStop => tripStop.StopId).ToArray();
        var stopsById = await _dbContext.Stops
            .AsNoTracking()
            .Where(stop => stopIds.Contains(stop.Id))
            .ToDictionaryAsync(stop => stop.Id, cancellationToken);
        if (stopsById.Count != stopIds.Distinct().Count())
        {
            return null;
        }

        var stops = tripStops
            .Select(tripStop =>
            {
                var stop = stopsById[tripStop.StopId];
                return new DriverTripRouteStopDto(
                    stop.Id,
                    stop.Name,
                    (double)stop.Latitude,
                    (double)stop.Longitude,
                    tripStop.OrderIndex,
                    tripStop.EstimatedArrivalTime,
                    tripStop.AllowPickup,
                    tripStop.AllowDropoff);
            })
            .ToArray();

        return new DriverTripRouteDto(
            routeProjection.TripId,
            routeProjection.RouteId,
            routeProjection.PathPolyline,
            ToStationDto(origin),
            ToStationDto(destination),
            stops);
    }

    private static DriverTripRouteStationDto ToStationDto(Station station) =>
        new(
            station.Id,
            station.Name,
            station.Latitude is { } latitude ? (double)latitude : null,
            station.Longitude is { } longitude ? (double)longitude : null);

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public async Task<TripCargoMutationResult?> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return await ExecuteCargoMutationAsync(tripId, async trip =>
        {
            var existing = await _dbContext.TripCargoParcels
                .FirstOrDefaultAsync(cargo => cargo.TripId == tripId && cargo.ParcelId == parcelId, cancellationToken);
            if (existing is not null)
            {
                return BuildCargoResult(trip, wasNearFullBefore: IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg));
            }

            ValidatePositiveCargo(weightKg, volumeM3);
            var reservedWeight = trip.ReservedParcelWeightKg + weightKg;
            var reservedVolume = trip.ReservedParcelVolumeM3 + volumeM3;
            EnsureCapacity(trip, reservedWeight, reservedVolume, trip.TotalLoadedWeightKg, trip.TotalLoadedVolumeM3, allowCapacityOverflow);

            await _dbContext.TripCargoParcels.AddAsync(TripCargoParcel.Reserve(tripId, parcelId, weightKg, volumeM3), cancellationToken);
            trip.UpdateCargoCounters(reservedWeight, reservedVolume, trip.TotalLoadedWeightKg, trip.TotalLoadedVolumeM3);
            trip.UpdatedAt = now;

            return BuildCargoResult(trip, wasNearFullBefore: IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg));
        }, now, cancellationToken);
    }

    public async Task<TripCargoMutationResult?> RemeasureReservedCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return await ExecuteCargoMutationAsync(tripId, async trip =>
        {
            ValidatePositiveCargo(weightKg, volumeM3);
            var cargo = await _dbContext.TripCargoParcels
                .FirstOrDefaultAsync(c => c.TripId == tripId && c.ParcelId == parcelId, cancellationToken);
            if (cargo is null)
            {
                await _dbContext.TripCargoParcels.AddAsync(TripCargoParcel.Reserve(tripId, parcelId, weightKg, volumeM3), cancellationToken);
                var initialReservedWeight = trip.ReservedParcelWeightKg + weightKg;
                var initialReservedVolume = trip.ReservedParcelVolumeM3 + volumeM3;
                EnsureCapacity(trip, initialReservedWeight, initialReservedVolume, trip.TotalLoadedWeightKg, trip.TotalLoadedVolumeM3, allowCapacityOverflow);
                trip.UpdateCargoCounters(initialReservedWeight, initialReservedVolume, trip.TotalLoadedWeightKg, trip.TotalLoadedVolumeM3);
                trip.UpdatedAt = now;
                return BuildCargoResult(trip, wasNearFullBefore: IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg));
            }

            var previous = cargo.Remeasure(weightKg, volumeM3);
            var reservedWeight = Math.Max(0m, trip.ReservedParcelWeightKg - previous.PreviousWeightKg + weightKg);
            var reservedVolume = Math.Max(0m, trip.ReservedParcelVolumeM3 - previous.PreviousVolumeM3 + volumeM3);
            EnsureCapacity(trip, reservedWeight, reservedVolume, trip.TotalLoadedWeightKg, trip.TotalLoadedVolumeM3, allowCapacityOverflow);
            trip.UpdateCargoCounters(reservedWeight, reservedVolume, trip.TotalLoadedWeightKg, trip.TotalLoadedVolumeM3);
            trip.UpdatedAt = now;

            return BuildCargoResult(trip, wasNearFullBefore: IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg));
        }, now, cancellationToken);
    }

    public async Task<TripCargoMutationResult?> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return await ExecuteCargoMutationAsync(tripId, async trip =>
        {
            var wasNearFull = IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg);
            var cargo = await _dbContext.TripCargoParcels
                .FirstOrDefaultAsync(c => c.TripId == tripId && c.ParcelId == parcelId, cancellationToken);

            if (cargo is null)
            {
                ValidatePositiveCargo(weightKg, volumeM3);
                cargo = TripCargoParcel.Reserve(tripId, parcelId, weightKg, volumeM3);
                await _dbContext.TripCargoParcels.AddAsync(cargo, cancellationToken);
                trip.UpdateCargoCounters(
                    trip.ReservedParcelWeightKg + cargo.WeightKg,
                    trip.ReservedParcelVolumeM3 + cargo.VolumeM3,
                    trip.TotalLoadedWeightKg,
                    trip.TotalLoadedVolumeM3);
            }

            if (cargo.State == TripCargoParcel.LoadedState)
            {
                return BuildCargoResult(trip, wasNearFull);
            }

            EnsureCapacity(
                trip,
                Math.Max(0m, trip.ReservedParcelWeightKg - cargo.WeightKg),
                Math.Max(0m, trip.ReservedParcelVolumeM3 - cargo.VolumeM3),
                trip.TotalLoadedWeightKg + cargo.WeightKg,
                trip.TotalLoadedVolumeM3 + cargo.VolumeM3,
                allowCapacityOverflow);

            cargo.MarkLoaded(now);
            trip.UpdateCargoCounters(
                Math.Max(0m, trip.ReservedParcelWeightKg - cargo.WeightKg),
                Math.Max(0m, trip.ReservedParcelVolumeM3 - cargo.VolumeM3),
                trip.TotalLoadedWeightKg + cargo.WeightKg,
                trip.TotalLoadedVolumeM3 + cargo.VolumeM3);
            trip.UpdatedAt = now;

            return BuildCargoResult(trip, wasNearFull);
        }, now, cancellationToken);
    }

    public async Task<TripCargoMutationResult?> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return await ExecuteCargoMutationAsync(tripId, async trip =>
        {
            var wasNearFull = IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg);
            var cargo = await _dbContext.TripCargoParcels
                .FirstOrDefaultAsync(c => c.TripId == tripId && c.ParcelId == parcelId, cancellationToken);
            if (cargo is null || cargo.State == TripCargoParcel.ReleasedState)
            {
                return BuildCargoResult(trip, wasNearFull);
            }

            var previousState = cargo.Release(now);
            var reservedWeight = previousState == TripCargoParcel.ReservedState
                ? Math.Max(0m, trip.ReservedParcelWeightKg - cargo.WeightKg)
                : trip.ReservedParcelWeightKg;
            var reservedVolume = previousState == TripCargoParcel.ReservedState
                ? Math.Max(0m, trip.ReservedParcelVolumeM3 - cargo.VolumeM3)
                : trip.ReservedParcelVolumeM3;
            var loadedWeight = previousState == TripCargoParcel.LoadedState
                ? Math.Max(0m, trip.TotalLoadedWeightKg - cargo.WeightKg)
                : trip.TotalLoadedWeightKg;
            var loadedVolume = previousState == TripCargoParcel.LoadedState
                ? Math.Max(0m, trip.TotalLoadedVolumeM3 - cargo.VolumeM3)
                : trip.TotalLoadedVolumeM3;

            trip.UpdateCargoCounters(reservedWeight, reservedVolume, loadedWeight, loadedVolume);
            trip.UpdatedAt = now;

            return BuildCargoResult(trip, wasNearFull);
        }, now, cancellationToken);
    }

    private async Task<TripCargoMutationResult?> ExecuteCargoMutationAsync(
        Guid tripId,
        Func<Domain.Entities.Trip, Task<TripCargoMutationResult>> mutate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var trip = await _dbContext.Trips.FirstOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM vietride_trip.trips WHERE id = {tripId} FOR UPDATE",
            cancellationToken);

        var result = await mutate(trip);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (ownsTransaction && transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    private static TripCargoMutationResult BuildCargoResult(Domain.Entities.Trip trip, bool wasNearFullBefore)
    {
        var max = trip.MaxCargoWeightKg ?? 0m;
        var percentFull = max <= 0m ? 0m : Math.Round(trip.TotalLoadedWeightKg / max * 100m, 2);
        return new TripCargoMutationResult(
            trip.Id,
            trip.ReservedParcelWeightKg,
            trip.ReservedParcelVolumeM3,
            trip.TotalLoadedWeightKg,
            trip.TotalLoadedVolumeM3,
            max,
            trip.MaxCargoVolumeM3 ?? 0m,
            percentFull,
            !wasNearFullBefore && IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg));
    }

    private static bool IsNearFull(decimal loadedWeightKg, decimal? maxCargoWeightKg)
        => maxCargoWeightKg is > 0m && loadedWeightKg >= maxCargoWeightKg.Value * 0.8m;

    private static void ValidatePositiveCargo(decimal weightKg, decimal volumeM3)
    {
        if (weightKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, "Cargo weight must be positive.");
        }

        if (volumeM3 <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(volumeM3), volumeM3, "Cargo volume must be positive.");
        }
    }

    private void EnsureCallerTransaction(string operation)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException($"A caller-owned transaction is required for {operation}.");
        }
    }

    private static long CreateVehicleDepartureLockKey(Guid vehicleId, DateTimeOffset departureDateTime)
    {
        Span<byte> input = stackalloc byte[24];
        vehicleId.TryWriteBytes(input[..16]);

        // PostgreSQL timestamptz is stored at microsecond precision. Hash the same normalized
        // instant that participates in uq_trips_vehicle_departure so equivalent offsets and
        // sub-microsecond .NET values cannot acquire different locks for the same database key.
        var utcMicroseconds = departureDateTime.ToUniversalTime().Ticks / 10;
        BinaryPrimitives.WriteInt64BigEndian(input[16..], utcMicroseconds);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    private static void EnsureCapacity(
        Domain.Entities.Trip trip,
        decimal reservedWeightKg,
        decimal reservedVolumeM3,
        decimal loadedWeightKg,
        decimal loadedVolumeM3,
        bool allowCapacityOverflow)
    {
        if (allowCapacityOverflow)
        {
            return;
        }

        if (trip.MaxCargoWeightKg.HasValue && reservedWeightKg + loadedWeightKg > trip.MaxCargoWeightKg.Value)
        {
            throw new InvalidOperationException("Trip cargo weight capacity would be exceeded.");
        }

        if (trip.MaxCargoVolumeM3.HasValue && reservedVolumeM3 + loadedVolumeM3 > trip.MaxCargoVolumeM3.Value)
        {
            throw new InvalidOperationException("Trip cargo volume capacity would be exceeded.");
        }
    }
}
