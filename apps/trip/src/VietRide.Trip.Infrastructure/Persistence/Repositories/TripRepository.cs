using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;
using VietRide.Trip.Application.Features.Internal.Reports.PlatformTrips;
using VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;
using VietRide.Trip.Application.Features.OperatorReports;
using VietRide.Trip.Application.Features.Trips.ListOperatorTrips;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Domain.Exceptions;

namespace VietRide.Trip.Infrastructure.Persistence.Repositories;

internal sealed class TripRepository : ITripRepository
{
    private readonly TripDbContext _dbContext;
    private readonly ILogger<TripRepository> _logger;

    public TripRepository(TripDbContext dbContext)
        : this(dbContext, NullLogger<TripRepository>.Instance)
    {
    }

    public TripRepository(
        TripDbContext dbContext,
        ILogger<TripRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task<Domain.Entities.Trip?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Trips.FindAsync(new object[] { id }, cancellationToken).AsTask();

    public async Task<IReadOnlyList<InternalTripSummaryDto>> ListSummariesByIdsAsync(
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            SELECT t.id,
                   t.status::text,
                   t.departure_date_time,
                   t.estimated_arrival_time,
                   r.id,
                   r.name,
                   origin.name,
                   destination.name,
                   v.id,
                   v.license_plate,
                   v.status::text,
                   vt.code,
                   vt.display_name,
                   t.driver_user_id,
                   t.assistant_user_id,
                   origin.id,
                   destination.id,
                   t.trip_code,
                   r.code
            FROM vietride_trip.trips AS t
            INNER JOIN vietride_trip.routes AS r ON r.id = t.route_id
            INNER JOIN vietride_trip.stations AS origin ON origin.id = r.origin_station_id
            INNER JOIN vietride_trip.stations AS destination ON destination.id = r.destination_station_id
            INNER JOIN vietride_trip.vehicles AS v ON v.id = t.vehicle_id
            INNER JOIN vietride_trip.vehicle_types AS vt ON vt.id = v.vehicle_type_id
            WHERE t.id = ANY(@trip_ids)
            ORDER BY t.id;
            """;
        AddParameter(command, "trip_ids", tripIds.ToArray());

        var summaries = new List<InternalTripSummaryDto>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                summaries.Add(new InternalTripSummaryDto(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    new InternalTripRouteSummaryDto(
                        reader.GetGuid(4),
                        reader.GetString(5),
                        reader.GetString(6),
                        reader.GetString(7))
                    {
                        OriginStationId = reader.GetGuid(15),
                        DestinationStationId = reader.GetGuid(16),
                        Code = reader.IsDBNull(18) ? null : reader.GetString(18),
                    },
                    new InternalTripVehicleSummaryDto(
                        reader.GetGuid(8),
                        reader.GetString(9),
                        reader.GetString(10),
                        new InternalTripVehicleTypeSummaryDto(
                            reader.GetString(11),
                            reader.GetString(12))),
                    reader.GetGuid(13),
                    reader.IsDBNull(14) ? null : reader.GetGuid(14))
                {
                    TripCode = reader.IsDBNull(17) ? null : reader.GetString(17),
                });
            }
        }

        if (summaries.Count == 0)
            return summaries;

        await using var stopCommand = connection.CreateCommand();
        stopCommand.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        stopCommand.CommandText =
            """
            SELECT ts.trip_id,
                   ts.stop_id,
                   s.name,
                   ts.order_index,
                   ts.estimated_arrival_time,
                   ts.status::text,
                   ts.actual_arrival_time,
                   ts.actual_departure_time
            FROM vietride_trip.trip_stops AS ts
            INNER JOIN vietride_trip.stops AS s ON s.id = ts.stop_id
            WHERE ts.trip_id = ANY(@trip_ids)
            ORDER BY ts.trip_id, ts.order_index, ts.stop_id;
            """;
        AddParameter(stopCommand, "trip_ids", summaries.Select(summary => summary.TripId).ToArray());

        var stopsByTrip = new Dictionary<Guid, List<InternalTripStopSummaryDto>>();
        await using (var stopReader = await stopCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await stopReader.ReadAsync(cancellationToken))
            {
                var tripId = stopReader.GetGuid(0);
                if (!stopsByTrip.TryGetValue(tripId, out var stops))
                {
                    stops = [];
                    stopsByTrip.Add(tripId, stops);
                }

                stops.Add(new InternalTripStopSummaryDto(
                    stopReader.GetGuid(1),
                    stopReader.GetString(2),
                    stopReader.GetInt32(3),
                    stopReader.GetFieldValue<DateTimeOffset>(4),
                    stopReader.GetString(5),
                    stopReader.IsDBNull(6) ? null : stopReader.GetFieldValue<DateTimeOffset>(6),
                    stopReader.IsDBNull(7) ? null : stopReader.GetFieldValue<DateTimeOffset>(7)));
            }
        }

        return summaries
            .Select(summary => summary with
            {
                Stops = stopsByTrip.TryGetValue(summary.TripId, out var stops)
                    ? stops
                    : [],
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<ForwardingTripCandidate>> ListForwardingCandidatesAsync(
        Guid operatorId,
        Guid? excludedTripId,
        string pickupLocationType,
        Guid pickupLocationId,
        string targetLocationType,
        Guid targetLocationId,
        decimal weightKg,
        decimal volumeM3,
        DateTimeOffset earliestDeparture,
        int limit,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            SELECT t.id,
                   CASE
                       WHEN @pickup_type = 'ROUTE_STOP' THEN pickup_stop.name
                       ELSE origin.name
                   END AS pickup_name,
                   CASE
                       WHEN @target_type = 'ROUTE_STOP' THEN target_stop.name
                       ELSE destination.name
                   END AS target_name,
                   COALESCE(pickup_trip_stop.estimated_arrival_time, t.departure_date_time) AS pickup_at,
                   COALESCE(target_trip_stop.estimated_arrival_time, t.estimated_arrival_time) AS target_eta,
                   ((COALESCE(t.max_cargo_weight_kg, 0) - t.estimated_passenger_luggage_kg - t.reserved_parcel_weight_kg - t.total_loaded_weight_kg) >= @weight_kg
                     AND (COALESCE(t.max_cargo_volume_m3, 0) - t.reserved_parcel_volume_m3 - t.total_loaded_volume_m3) >= @volume_m3) AS has_capacity
            FROM vietride_trip.trips AS t
            INNER JOIN vietride_trip.routes AS r ON r.id = t.route_id
            INNER JOIN vietride_trip.stations AS origin ON origin.id = r.origin_station_id
            INNER JOIN vietride_trip.stations AS destination ON destination.id = r.destination_station_id
            LEFT JOIN vietride_trip.trip_stops AS pickup_trip_stop
             ON pickup_trip_stop.trip_id = t.id
             AND pickup_trip_stop.stop_id = @pickup_id
            LEFT JOIN vietride_trip.stops AS pickup_stop ON pickup_stop.id = pickup_trip_stop.stop_id
            LEFT JOIN vietride_trip.trip_stops AS target_trip_stop
             ON target_trip_stop.trip_id = t.id
             AND target_trip_stop.stop_id = @target_id
            LEFT JOIN vietride_trip.stops AS target_stop ON target_stop.id = target_trip_stop.stop_id
            WHERE t.operator_id = @operator_id
              AND (@excluded_trip_id = '00000000-0000-0000-0000-000000000000'::uuid OR t.id <> @excluded_trip_id)
              AND t.status IN ('SCHEDULED', 'BOARDING')
              AND t.departure_date_time >= @earliest_departure
              AND (
                    (@pickup_type = 'ROUTE_STOP' AND pickup_trip_stop.stop_id IS NOT NULL AND pickup_trip_stop.allow_pickup = TRUE)
                 OR (@pickup_type <> 'ROUTE_STOP' AND r.origin_station_id = @pickup_id)
              )
              AND (
                    (@target_type = 'ROUTE_STOP' AND target_trip_stop.stop_id IS NOT NULL AND target_trip_stop.allow_dropoff = TRUE)
                 OR (@target_type <> 'ROUTE_STOP' AND r.destination_station_id = @target_id)
              )
              AND (
                    @pickup_type <> 'ROUTE_STOP'
                 OR @target_type <> 'ROUTE_STOP'
                 OR pickup_trip_stop.order_index < target_trip_stop.order_index
              )
            ORDER BY has_capacity DESC, pickup_at, t.id
            LIMIT @limit;
            """;
        AddParameter(command, "operator_id", operatorId);
        AddParameter(command, "excluded_trip_id", excludedTripId ?? Guid.Empty);
        AddParameter(command, "pickup_type", pickupLocationType.ToUpperInvariant());
        AddParameter(command, "pickup_id", pickupLocationId);
        AddParameter(command, "target_type", targetLocationType.ToUpperInvariant());
        AddParameter(command, "target_id", targetLocationId);
        AddParameter(command, "weight_kg", weightKg);
        AddParameter(command, "volume_m3", volumeM3);
        AddParameter(command, "earliest_departure", earliestDeparture);
        AddParameter(command, "limit", Math.Clamp(limit, 1, 50));

        var results = new List<ForwardingTripCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ForwardingTripCandidate(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetBoolean(5)));
        }
        return results;
    }

    public async Task<PagedResult<OperatorTripListRow>> ListOperatorTripsAsync(
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
    {
        var filters = new List<string>
        {
            "t.operator_id = @operator_id",
            "r.operator_id = @operator_id",
            "v.operator_id = @operator_id",
        };

        if (status.HasValue)
        {
            filters.Add("t.status = CAST(@status AS vietride_trip.trip_status)");
        }

        if (fromUtc.HasValue)
        {
            filters.Add("t.departure_date_time >= @from_utc");
        }

        if (toUtc.HasValue)
        {
            filters.Add("t.departure_date_time < @to_utc");
        }

        var routePattern = routeSearch is null ? null : $"%{EscapeLikePattern(routeSearch)}%";
        var codePrefixPattern = routeSearch is null ? null : $"{EscapeLikePattern(routeSearch.Trim().ToUpperInvariant())}%";
        var platePattern = normalizedPlateSearch is null ? null : $"%{normalizedPlateSearch}%";
        if (routeSearch is not null || normalizedPlateSearch is not null)
        {
            var searchFilters = new List<string>();
            if (routePattern is not null)
            {
                searchFilters.Add("r.name ILIKE @route_pattern ESCAPE '\\'");
                searchFilters.Add("t.trip_code LIKE @code_prefix_pattern ESCAPE '\\'");
                searchFilters.Add("r.code LIKE @code_prefix_pattern ESCAPE '\\'");
            }

            if (platePattern is not null)
            {
                searchFilters.Add(
                    "regexp_replace(v.license_plate, '[^0-9A-Za-z]', '', 'g') ILIKE @plate_pattern");
            }

            filters.Add($"({string.Join(" OR ", searchFilters)})");
        }

        var whereSql = string.Join(" AND ", filters);
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var countCommand = connection.CreateCommand();
        countCommand.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        countCommand.CommandText = $"""
            SELECT COUNT(*)
            FROM vietride_trip.trips AS t
            INNER JOIN vietride_trip.routes AS r ON r.id = t.route_id
            INNER JOIN vietride_trip.vehicles AS v ON v.id = t.vehicle_id
            INNER JOIN vietride_trip.stations AS origin ON origin.id = r.origin_station_id
            INNER JOIN vietride_trip.stations AS destination ON destination.id = r.destination_station_id
            WHERE {whereSql};
            """;
        AddOperatorTripParameters(
            countCommand,
            operatorId,
            routePattern,
            codePrefixPattern,
            platePattern,
            status,
            fromUtc,
            toUtc);
        var totalItems = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));

        var direction = sortDescending ? "DESC" : "ASC";
        await using var itemsCommand = connection.CreateCommand();
        itemsCommand.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        itemsCommand.CommandText = $"""
            SELECT t.id,
                   t.status::text,
                   r.id,
                   r.name,
                   origin.name,
                   destination.name,
                   v.id,
                   v.license_plate,
                   v.status::text,
                   t.driver_user_id,
                   t.assistant_user_id,
                   t.departure_date_time,
                   t.estimated_arrival_time,
                   t.driver_schedule_id,
                   t.trip_code,
                   r.code
            FROM vietride_trip.trips AS t
            INNER JOIN vietride_trip.routes AS r ON r.id = t.route_id
            INNER JOIN vietride_trip.vehicles AS v ON v.id = t.vehicle_id
            INNER JOIN vietride_trip.stations AS origin ON origin.id = r.origin_station_id
            INNER JOIN vietride_trip.stations AS destination ON destination.id = r.destination_station_id
            WHERE {whereSql}
            ORDER BY t.departure_date_time {direction}, t.id {direction}
            LIMIT @page_size OFFSET @offset;
            """;
        AddOperatorTripParameters(
            itemsCommand,
            operatorId,
            routePattern,
            codePrefixPattern,
            platePattern,
            status,
            fromUtc,
            toUtc);
        AddParameter(itemsCommand, "page_size", pageSize);
        AddParameter(itemsCommand, "offset", checked((page - 1) * pageSize));

        var items = new List<OperatorTripListRow>();
        await using var reader = await itemsCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new OperatorTripListRow(
                reader.GetGuid(0),
                Enum.Parse<TripStatus>(reader.GetString(1)),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetGuid(6),
                reader.GetString(7),
                Enum.Parse<VehicleStatus>(reader.GetString(8)),
                reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetFieldValue<DateTimeOffset>(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15)));
        }

        return PagedResult<OperatorTripListRow>.Create(items, page, pageSize, totalItems);
    }

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
            WITH live AS (
                SELECT operator_id, COUNT(*) AS completed_trip_count
                FROM vietride_trip.trips
                WHERE status = 'COMPLETED'::vietride_trip.trip_status
                  AND completed_at >= @from_utc
                  AND completed_at < @to_utc
                GROUP BY operator_id
            ),
            projected AS (
                SELECT operator_id, COUNT(*) AS completed_trip_count
                FROM vietride_trip.platform_trip_stats
                WHERE completed_at >= @from_utc
                  AND completed_at < @to_utc
                GROUP BY operator_id
            )
            SELECT COALESCE(live.operator_id, projected.operator_id) AS operator_id,
                   COALESCE(live.completed_trip_count, 0) AS live_count,
                   COALESCE(projected.completed_trip_count, 0) AS projected_count
            FROM live
            FULL OUTER JOIN projected USING (operator_id)
            ORDER BY operator_id;
            """;
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        AddParameter(command, "from_utc", fromUtc.ToUniversalTime());
        AddParameter(command, "to_utc", toUtc.ToUniversalTime());

        var items = new List<PlatformTripReportItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var operatorId = reader.GetGuid(0);
            var completedTripCount = reader.GetInt64(1);
            var projectedCount = reader.GetInt64(2);
            if (completedTripCount != projectedCount)
            {
                _logger.LogError(
                    "Platform TripStats mismatch for operator {OperatorId}: live count {LiveCount}, projected count {ProjectedCount}",
                    operatorId,
                    completedTripCount,
                    projectedCount);
                throw new PlatformTripStatsMismatchException();
            }

            items.Add(new PlatformTripReportItem(operatorId, completedTripCount));
        }

        return items;
    }

    public async IAsyncEnumerable<TripOperatorOccupancyRow> StreamOperatorOccupancyRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rows = from trip in _dbContext.Trips.AsNoTracking()
            join route in _dbContext.Routes.AsNoTracking() on trip.RouteId equals route.Id
            join vehicle in _dbContext.Vehicles.AsNoTracking() on trip.VehicleId equals vehicle.Id
            where trip.OperatorId == operatorId
                && trip.DepartureDateTime >= fromUtc
                && trip.DepartureDateTime < toUtc
            orderby trip.DepartureDateTime, trip.Id
            select new TripOperatorOccupancyRow(
                trip.Id,
                trip.RouteId,
                trip.TripCode ?? string.Empty,
                route.Name,
                vehicle.LicensePlate,
                trip.Status,
                trip.DepartureDateTime,
                trip.Seats.LongCount(seat => seat.SeatType != TripSeatType.DRIVER_AREA
                    && seat.Status != TripSeatStatus.UNAVAILABLE),
                trip.Seats.LongCount(seat => seat.Status == TripSeatStatus.BOOKED));

        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return row;
        }
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

    public Task<Domain.Entities.Trip?> GetRouteChangePreflightAsync(
        Guid tripId,
        CancellationToken cancellationToken)
        => _dbContext.Trips
            .AsNoTracking()
            .SingleOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);

    public async Task<Domain.Entities.Trip?> AcquireForRouteChangeAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        EnsureCallerTransaction("route-change Trip acquisition");

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM vietride_trip.trips WHERE id = {tripId} FOR UPDATE",
            cancellationToken);
        var trip = await _dbContext.Trips.SingleOrDefaultAsync(
            item => item.Id == tripId,
            cancellationToken);
        if (trip is not null)
        {
            await _dbContext.Entry(trip).ReloadAsync(cancellationToken);
        }

        return trip;
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

    private static string EscapeLikePattern(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static void AddOperatorTripParameters(
        System.Data.Common.DbCommand command,
        Guid operatorId,
        string? routePattern,
        string? codePrefixPattern,
        string? platePattern,
        TripStatus? status,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        AddParameter(command, "operator_id", operatorId);
        if (routePattern is not null)
        {
            AddParameter(command, "route_pattern", routePattern);
            AddParameter(command, "code_prefix_pattern", codePrefixPattern!);
        }

        if (platePattern is not null)
        {
            AddParameter(command, "plate_pattern", platePattern);
        }

        if (status.HasValue)
        {
            AddParameter(command, "status", status.Value.ToString());
        }

        if (fromUtc.HasValue)
        {
            AddParameter(command, "from_utc", fromUtc.Value.ToUniversalTime());
        }

        if (toUtc.HasValue)
        {
            AddParameter(command, "to_utc", toUtc.Value.ToUniversalTime());
        }
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
                if (existing.State == TripCargoParcel.ReleasedState)
                {
                    ValidatePositiveCargo(weightKg, volumeM3);
                    var restoredReservedWeight = trip.ReservedParcelWeightKg + weightKg;
                    var restoredReservedVolume = trip.ReservedParcelVolumeM3 + volumeM3;
                    EnsureCapacity(
                        trip,
                        restoredReservedWeight,
                        restoredReservedVolume,
                        trip.TotalLoadedWeightKg,
                        trip.TotalLoadedVolumeM3,
                        allowCapacityOverflow);
                    existing.RestoreReservation(weightKg, volumeM3);
                    trip.UpdateCargoCounters(
                        restoredReservedWeight,
                        restoredReservedVolume,
                        trip.TotalLoadedWeightKg,
                        trip.TotalLoadedVolumeM3);
                    trip.UpdatedAt = now;
                }

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

    public async Task<TripCargoTransferRepositoryResult> TransferCargoAsync(
        Guid sourceTripId,
        Guid parcelId,
        Guid targetTripId,
        string targetState,
        bool allowCapacityOverflow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        EnsureCallerTransaction("cargo transfer");
        if (sourceTripId == targetTripId)
        {
            return TripCargoTransferRepositoryResult.Failed(TripCargoTransferStatus.CONFLICT);
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT id
            FROM vietride_trip.trips
            WHERE id = {sourceTripId} OR id = {targetTripId}
            ORDER BY id
            FOR UPDATE
            """,
            cancellationToken);

        var trips = await _dbContext.Trips
            .Where(trip => trip.Id == sourceTripId || trip.Id == targetTripId)
            .ToArrayAsync(cancellationToken);
        if (trips.Length != 2)
        {
            return TripCargoTransferRepositoryResult.Failed(TripCargoTransferStatus.TRIP_NOT_FOUND);
        }

        var sourceTrip = trips.Single(trip => trip.Id == sourceTripId);
        var targetTrip = trips.Single(trip => trip.Id == targetTripId);
        if (sourceTrip.OperatorId != targetTrip.OperatorId)
        {
            return TripCargoTransferRepositoryResult.Failed(TripCargoTransferStatus.CONFLICT);
        }

        if (targetState == TripCargoParcel.LoadedState
            && allowCapacityOverflow
            && targetTrip.Source != TripSource.VEHICLE_SUBSTITUTION)
        {
            return TripCargoTransferRepositoryResult.Failed(
                TripCargoTransferStatus.OVERFLOW_NOT_ALLOWED);
        }

        var sourceCargo = await _dbContext.TripCargoParcels
            .SingleOrDefaultAsync(
                cargo => cargo.TripId == sourceTripId && cargo.ParcelId == parcelId,
                cancellationToken);
        if (sourceCargo is null)
        {
            return TripCargoTransferRepositoryResult.Failed(TripCargoTransferStatus.SOURCE_CARGO_NOT_FOUND);
        }

        if (sourceCargo.State == TripCargoParcel.ReleasedState)
        {
            return TripCargoTransferRepositoryResult.Failed(TripCargoTransferStatus.CONFLICT);
        }

        var targetCargo = await _dbContext.TripCargoParcels
            .SingleOrDefaultAsync(
                cargo => cargo.TripId == targetTripId && cargo.ParcelId == parcelId,
                cancellationToken);
        if (targetCargo is not null && targetCargo.State != TripCargoParcel.ReleasedState)
        {
            return TripCargoTransferRepositoryResult.Failed(TripCargoTransferStatus.CONFLICT);
        }

        var targetReservedWeight = targetTrip.ReservedParcelWeightKg;
        var targetReservedVolume = targetTrip.ReservedParcelVolumeM3;
        var targetLoadedWeight = targetTrip.TotalLoadedWeightKg;
        var targetLoadedVolume = targetTrip.TotalLoadedVolumeM3;
        if (targetState == TripCargoParcel.ReservedState)
        {
            targetReservedWeight += sourceCargo.WeightKg;
            targetReservedVolume += sourceCargo.VolumeM3;
        }
        else
        {
            targetLoadedWeight += sourceCargo.WeightKg;
            targetLoadedVolume += sourceCargo.VolumeM3;
        }

        try
        {
            EnsureCapacity(
                targetTrip,
                targetReservedWeight,
                targetReservedVolume,
                targetLoadedWeight,
                targetLoadedVolume,
                targetState == TripCargoParcel.LoadedState && allowCapacityOverflow);
        }
        catch (InvalidOperationException)
        {
            return TripCargoTransferRepositoryResult.Failed(TripCargoTransferStatus.CAPACITY_EXCEEDED);
        }

        var targetWasNearFull = IsNearFull(
            targetTrip.TotalLoadedWeightKg,
            targetTrip.MaxCargoWeightKg);
        var sourcePreviousState = sourceCargo.Release(now);
        sourceTrip.UpdateCargoCounters(
            sourcePreviousState == TripCargoParcel.ReservedState
                ? Math.Max(0m, sourceTrip.ReservedParcelWeightKg - sourceCargo.WeightKg)
                : sourceTrip.ReservedParcelWeightKg,
            sourcePreviousState == TripCargoParcel.ReservedState
                ? Math.Max(0m, sourceTrip.ReservedParcelVolumeM3 - sourceCargo.VolumeM3)
                : sourceTrip.ReservedParcelVolumeM3,
            sourcePreviousState == TripCargoParcel.LoadedState
                ? Math.Max(0m, sourceTrip.TotalLoadedWeightKg - sourceCargo.WeightKg)
                : sourceTrip.TotalLoadedWeightKg,
            sourcePreviousState == TripCargoParcel.LoadedState
                ? Math.Max(0m, sourceTrip.TotalLoadedVolumeM3 - sourceCargo.VolumeM3)
                : sourceTrip.TotalLoadedVolumeM3);

        if (targetCargo is null)
        {
            targetCargo = TripCargoParcel.Reserve(
                targetTripId,
                parcelId,
                sourceCargo.WeightKg,
                sourceCargo.VolumeM3);
            await _dbContext.TripCargoParcels.AddAsync(targetCargo, cancellationToken);
        }
        else
        {
            targetCargo.RestoreReservation(sourceCargo.WeightKg, sourceCargo.VolumeM3);
        }

        if (targetState == TripCargoParcel.LoadedState)
        {
            targetCargo.MarkLoaded(now);
        }

        targetTrip.UpdateCargoCounters(
            targetReservedWeight,
            targetReservedVolume,
            targetLoadedWeight,
            targetLoadedVolume);
        sourceTrip.UpdatedAt = now;
        targetTrip.UpdatedAt = now;

        var targetMaxWeight = targetTrip.MaxCargoWeightKg ?? 0m;
        var targetPercentFull = targetMaxWeight <= 0m
            ? 0m
            : Math.Round(targetTrip.TotalLoadedWeightKg / targetMaxWeight * 100m, 2);
        return new TripCargoTransferRepositoryResult(
            TripCargoTransferStatus.SUCCESS,
            parcelId,
            sourceTripId,
            targetTripId,
            targetState,
            sourceCargo.WeightKg,
            sourceCargo.VolumeM3,
            !targetWasNearFull && IsNearFull(targetTrip.TotalLoadedWeightKg, targetTrip.MaxCargoWeightKg),
            targetTrip.OperatorId,
            targetTrip.TotalLoadedWeightKg,
            targetMaxWeight,
            targetPercentFull);
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

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM vietride_trip.trips WHERE id = {tripId} FOR UPDATE",
            cancellationToken);

        var trip = await _dbContext.Trips.FirstOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

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
            !wasNearFullBefore && IsNearFull(trip.TotalLoadedWeightKg, trip.MaxCargoWeightKg),
            trip.OperatorId);
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

        if (trip.MaxCargoWeightKg.HasValue
            && trip.EstimatedPassengerLuggageKg + reservedWeightKg + loadedWeightKg > trip.MaxCargoWeightKg.Value)
        {
            throw new TripCargoCapacityExceededException("Trip cargo weight capacity would be exceeded.");
        }

        if (trip.MaxCargoVolumeM3.HasValue && reservedVolumeM3 + loadedVolumeM3 > trip.MaxCargoVolumeM3.Value)
        {
            throw new TripCargoCapacityExceededException("Trip cargo volume capacity would be exceeded.");
        }
    }
}
