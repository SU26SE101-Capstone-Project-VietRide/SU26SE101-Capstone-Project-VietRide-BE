using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Internal.Bookings;
using VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Booking.Application.Features.OperatorReports;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for the Booking aggregate.
/// Implements <see cref="IBookingRepository"/> — extends the generic repository contract
/// (<see cref="IRepository{TEntity,TId}"/>) with Booking-specific queries.
/// </summary>
internal sealed class BookingRepository : IBookingRepository
{
    private readonly BookingDbContext _db;
    private readonly ILogger<BookingRepository> _logger;

    public BookingRepository(BookingDbContext db)
        : this(db, NullLogger<BookingRepository>.Instance)
    {
    }

    public BookingRepository(
        BookingDbContext db,
        ILogger<BookingRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // IRepository<Booking, Guid>
    // -----------------------------------------------------------------------

    public async Task<BookingEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<BookingEntity> AddAsync(BookingEntity entity, CancellationToken ct)
    {
        await _db.Bookings.AddAsync(entity, ct);
        return entity;
    }

    public void Update(BookingEntity entity)
        => _db.Bookings.Update(entity);

    public void Remove(BookingEntity entity)
        => _db.Bookings.Remove(entity);

    public IQueryable<BookingEntity> Query()
        => _db.Bookings;

    public IQueryable<BookingEntity> QueryNoTracking()
        => _db.Bookings.AsNoTracking();

    public async Task<PagedResult<BookingEntity>> ListPassengerHistoryAsync(
        Guid passengerUserId,
        BookingStatus? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Bookings
            .AsNoTracking()
            .Where(booking => booking.PassengerUserId == passengerUserId);

        if (status.HasValue)
            query = query.Where(booking => booking.Status == status.Value);
        if (from.HasValue)
            query = query.Where(booking => booking.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(booking => booking.CreatedAt < to.Value);

        var totalItems = await query.LongCountAsync(ct);
        var items = await query
            .Include(booking => booking.Tickets)
            .AsSplitQuery()
            .OrderByDescending(booking => booking.CreatedAt)
            .ThenByDescending(booking => booking.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<BookingEntity>.Create(items, page, pageSize, totalItems);
    }

    // -----------------------------------------------------------------------
    // IBookingRepository — aggregate-specific queries
    // -----------------------------------------------------------------------

    public Task AcquireEventLockAsync(Guid sourceEventId, CancellationToken ct = default)
        => _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({sourceEventId.ToString("N")}, 0))",
            ct);

    public async Task AcquirePaymentTransitionLocksAsync(
        IReadOnlyCollection<Guid> bookingIds,
        CancellationToken ct = default)
    {
        foreach (var bookingId in bookingIds.Distinct().Order())
        {
            var lockKey = $"booking-payment-transition:{bookingId:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                    ct)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<BookingEntity>> GetScheduleChangeBookingsForUpdateAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.Bookings
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_booking.bookings
                WHERE trip_id = {tripId}
                  AND operator_id = {operatorId}
                  AND status IN (
                      CAST('PENDING_PAYMENT' AS public.booking_status),
                      CAST('CONFIRMED' AS public.booking_status))
                ORDER BY id
                FOR UPDATE
                """)
            .ToListAsync(ct);

    public async Task<bool> TryAdvanceTripCurrentDepartureAsync(
        Guid bookingId,
        DateTimeOffset expectedDeparture,
        DateTimeOffset newDeparture,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
    {
        var expectedUtc = NormalizeToPostgresTimestamp(expectedDeparture);
        var newUtc = NormalizeToPostgresTimestamp(newDeparture);
        var updatedUtc = NormalizeToPostgresTimestamp(updatedAt);
        return await _db.Bookings
            .Where(booking => booking.Id == bookingId
                && (booking.Status == BookingStatus.PENDING_PAYMENT
                    || booking.Status == BookingStatus.CONFIRMED)
                && booking.TripCurrentDeparture == expectedUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    booking => booking.TripCurrentDeparture,
                    newUtc)
                .SetProperty(
                    booking => booking.UpdatedAt,
                    updatedUtc), ct) == 1;
    }

    private static DateTimeOffset NormalizeToPostgresTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    public async Task<IReadOnlyList<BookingEntity>> GetConfirmedByTripAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.Bookings
            .Where(booking => booking.TripId == tripId
                && booking.OperatorId == operatorId
                && booking.Status == BookingStatus.CONFIRMED)
            .OrderBy(booking => booking.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BookingEntity>> GetCancellableByTripAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.Bookings
            .Include(booking => booking.Tickets)
            .Include(booking => booking.ShuttleIntent)
            .Where(booking => booking.TripId == tripId
                && booking.OperatorId == operatorId
                && (booking.Status == BookingStatus.PENDING_PAYMENT
                    || booking.Status == BookingStatus.CONFIRMED))
            .OrderBy(booking => booking.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BookingEntity>> GetDisruptionBookingsForUpdateAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => await _db.Bookings
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_booking.bookings
                WHERE trip_id = {tripId}
                  AND operator_id = {operatorId}
                  AND status IN (
                      CAST('CONFIRMED' AS public.booking_status),
                      CAST('PARTIAL_NO_SHOW' AS public.booking_status))
                ORDER BY id
                FOR UPDATE
                """)
            .Include(booking => booking.Tickets)
            .Include(booking => booking.ShuttleIntent)
            .AsSplitQuery()
            .ToListAsync(ct);

    public Task<bool> HasOutboxEventAsync(
        string eventType,
        Guid eventId,
        CancellationToken ct = default)
    {
        var fragment = JsonSerializer.Serialize(
            new { eventId },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return _db.OutboxEvents.AnyAsync(
            row => row.EventType == eventType && EF.Functions.JsonContains(row.Payload, fragment),
            ct);
    }

    /// <inheritdoc/>
    public async Task<TripEditImpactDto> GetTripEditImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator id must be non-empty.", nameof(operatorId));
        }

        var activeBookings = await _db.Bookings
            .AsNoTracking()
            .Where(booking => booking.TripId == tripId && booking.OperatorId == operatorId)
            .Where(booking => booking.Status == BookingStatus.PENDING_PAYMENT
                || booking.Status == BookingStatus.CONFIRMED)
            .OrderBy(booking => booking.Id)
            .Select(booking => new
            {
                BookingId = booking.Id,
                booking.Status,
                TotalAmount = booking.TotalAmount.Amount,
            })
            .ToListAsync(ct);

        if (activeBookings.Count == 0)
        {
            return new TripEditImpactDto(tripId, 0, []);
        }

        var bookingIds = activeBookings.Select(booking => booking.BookingId).ToArray();
        var seatRows = await _db.Passengers
            .AsNoTracking()
            .Where(passenger => bookingIds.Contains(passenger.BookingId))
            .OrderBy(passenger => passenger.BookingId)
            .ThenBy(passenger => passenger.SeatNumber)
            .Select(passenger => new
            {
                passenger.BookingId,
                passenger.SeatNumber,
            })
            .ToListAsync(ct);

        var seatsByBooking = seatRows
            .GroupBy(row => row.BookingId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(row => row.SeatNumber)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());

        var impacts = activeBookings
            .Select(booking => new TripEditImpactDto.ActiveBooking(
                booking.BookingId,
                booking.Status.ToString(),
                seatsByBooking.GetValueOrDefault(booking.BookingId, []),
                booking.TotalAmount))
            .ToArray();

        return new TripEditImpactDto(tripId, impacts.Length, impacts);
    }

    /// <inheritdoc/>
    public async Task<VehicleSubstitutionImpactDto> GetVehicleSubstitutionImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
    {
        if (tripId == Guid.Empty)
        {
            throw new ArgumentException("Trip id must be non-empty.", nameof(tripId));
        }

        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator id must be non-empty.", nameof(operatorId));
        }

        var eligibleBookings = await _db.Bookings
            .AsNoTracking()
            .Where(booking => booking.TripId == tripId && booking.OperatorId == operatorId)
            .Where(booking => booking.Status == BookingStatus.CONFIRMED
                || booking.Status == BookingStatus.PARTIAL_NO_SHOW)
            .OrderBy(booking => booking.Id)
            .Select(booking => new
            {
                BookingId = booking.Id,
                booking.Status,
            })
            .ToListAsync(ct);

        if (eligibleBookings.Count == 0)
        {
            return new VehicleSubstitutionImpactDto(tripId, operatorId, []);
        }

        var bookingIds = eligibleBookings
            .Select(booking => booking.BookingId)
            .ToArray();
        var eligiblePassengers = await _db.Passengers
            .AsNoTracking()
            .Where(passenger => bookingIds.Contains(passenger.BookingId))
            .Where(passenger => passenger.BoardingStatus == PassengerBoardingStatus.BOARDED
                || passenger.BoardingStatus == PassengerBoardingStatus.PENDING)
            .OrderBy(passenger => passenger.BookingId)
            .ThenBy(passenger => passenger.Id)
            .Select(passenger => new
            {
                passenger.BookingId,
                PassengerId = passenger.Id,
                passenger.BoardingStatus,
                OriginalSeatNumber = passenger.SeatNumber ?? string.Empty,
            })
            .ToListAsync(ct);

        var passengersByBooking = eligiblePassengers
            .GroupBy(row => row.BookingId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<VehicleSubstitutionImpactDto.PassengerImpact>)group
                    .Select(row => new VehicleSubstitutionImpactDto.PassengerImpact(
                        row.PassengerId,
                        row.BoardingStatus.ToString(),
                        string.IsNullOrEmpty(row.OriginalSeatNumber)
                            ? null
                            : row.OriginalSeatNumber))
                    .ToArray());

        var bookingImpacts = eligibleBookings
            .Select(booking => new VehicleSubstitutionImpactDto.BookingImpact(
                booking.BookingId,
                booking.Status.ToString(),
                passengersByBooking.GetValueOrDefault(booking.BookingId, [])))
            .ToArray();

        return new VehicleSubstitutionImpactDto(tripId, operatorId, bookingImpacts);
    }

    public async Task<IReadOnlyList<BookingEntity>> GetVehicleSubstitutionBookingsForUpdateAsync(
        Guid oldTripId,
        Guid operatorId,
        IReadOnlyCollection<Guid> bookingIds,
        CancellationToken ct = default)
    {
        if (oldTripId == Guid.Empty)
            throw new ArgumentException("Original Trip id must be non-empty.", nameof(oldTripId));
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id must be non-empty.", nameof(operatorId));
        if (bookingIds.Count == 0)
            return [];

        var ids = bookingIds.Distinct().ToArray();
        return await _db.Bookings
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_booking.bookings
                WHERE id = ANY({ids})
                  AND trip_id = {oldTripId}
                  AND operator_id = {operatorId}
                  AND status IN (
                      CAST('CONFIRMED' AS public.booking_status),
                      CAST('PARTIAL_NO_SHOW' AS public.booking_status))
                ORDER BY id
                FOR UPDATE
                """)
            .Include(booking => booking.Passengers)
            .Include(booking => booking.Tickets)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public Task<int> GetPendingPassengerCountAsync(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        CancellationToken ct = default)
        => _db.Passengers
            .AsNoTracking()
            .CountAsync(passenger => passenger.BoardingStatus == PassengerBoardingStatus.PENDING
                && passenger.Booking != null
                && passenger.Booking.Status == BookingStatus.CONFIRMED
                && passenger.Booking.TripId == tripId
                && passenger.Booking.PickupStopId == stopId
                && passenger.Booking.OperatorId == operatorId, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PlatformBookingReportItem>> GetPlatformBookingMetricsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct = default)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH live AS (
                SELECT operator_id,
                       COUNT(*)::numeric AS completed_booking_count,
                       COALESCE(SUM(total_amount), 0)::numeric AS booking_revenue_vnd
                FROM vietride_booking.bookings
                WHERE status = 'COMPLETED'::public.booking_status
                  AND completed_at >= @from_utc
                  AND completed_at < @to_utc
                GROUP BY operator_id
            ),
            projected AS (
                SELECT operator_id,
                       COUNT(*)::numeric AS completed_booking_count,
                       COALESCE(SUM(booking_revenue_vnd), 0)::numeric AS booking_revenue_vnd
                FROM vietride_booking.platform_booking_stats
                WHERE completed_at >= @from_utc
                  AND completed_at < @to_utc
                GROUP BY operator_id
            )
            SELECT COALESCE(live.operator_id, projected.operator_id) AS operator_id,
                   COALESCE(live.completed_booking_count, 0)::numeric AS live_count,
                   COALESCE(live.booking_revenue_vnd, 0)::numeric AS live_revenue,
                   COALESCE(projected.completed_booking_count, 0)::numeric AS projected_count,
                   COALESCE(projected.booking_revenue_vnd, 0)::numeric AS projected_revenue
            FROM live
            FULL OUTER JOIN projected USING (operator_id)
            ORDER BY operator_id;
            """;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        AddParameter(command, "from_utc", fromUtc.ToUniversalTime());
        AddParameter(command, "to_utc", toUtc.ToUniversalTime());

        var items = new List<PlatformBookingReportItem>();
        long totalCount = 0;
        long totalRevenue = 0;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var completedBookingCount = checked((long)reader.GetDecimal(1));
                var bookingRevenueVnd = checked((long)reader.GetDecimal(2));
                var projectedCount = checked((long)reader.GetDecimal(3));
                var projectedRevenue = checked((long)reader.GetDecimal(4));
                var operatorId = reader.GetGuid(0);

                if (completedBookingCount != projectedCount
                    || bookingRevenueVnd != projectedRevenue)
                {
                    _logger.LogError(
                        "Platform BookingStats mismatch for operator {OperatorId}: live count {LiveCount}, projected count {ProjectedCount}, live revenue {LiveRevenueVnd}, projected revenue {ProjectedRevenueVnd}",
                        operatorId,
                        completedBookingCount,
                        projectedCount,
                        bookingRevenueVnd,
                        projectedRevenue);
                    throw new PlatformBookingStatsMismatchException();
                }

                totalCount = checked(totalCount + completedBookingCount);
                totalRevenue = checked(totalRevenue + bookingRevenueVnd);
                items.Add(new PlatformBookingReportItem(
                    operatorId,
                    completedBookingCount,
                    bookingRevenueVnd));
            }
        }
        catch (OverflowException exception)
        {
            throw new PlatformReportValueOverflowException(exception);
        }

        return items;
    }

    public async IAsyncEnumerable<BookingOperatorReportRow> StreamOperatorReportRowsAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        bool cancellationOnly,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = _db.Bookings
            .AsNoTracking()
            .Where(booking => booking.OperatorId == operatorId);

        query = cancellationOnly
            ? query.Where(booking => booking.CancelledAt >= fromUtc && booking.CancelledAt < toUtc)
            : query.Where(booking => booking.CreatedAt >= fromUtc && booking.CreatedAt < toUtc);

        var rows = query
            .OrderBy(booking => cancellationOnly ? booking.CancelledAt : booking.CreatedAt)
            .ThenBy(booking => booking.Id)
            .Select(booking => new BookingOperatorReportRow(
                booking.Id,
                booking.BookingCode.Value,
                booking.TripId,
                booking.Status.ToString(),
                booking.Passengers.Count,
                booking.TotalAmount.Amount,
                booking.CreatedAt,
                booking.ConfirmedAt,
                booking.CompletedAt,
                booking.CancelledAt,
                booking.CancellationReason == null ? null : booking.CancellationReason.ToString()));

        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
        {
            yield return row;
        }
    }

    /// <inheritdoc/>
    public async Task<OperatorBookingDetailDto?> GetOperatorBookingDetailAsync(
        Guid bookingId, Guid operatorId, CancellationToken ct = default)
    {
        var booking = await _db.Bookings.AsNoTracking()
            .Where(row => row.Id == bookingId && row.OperatorId == operatorId)
            .Select(row => new
            {
                row.Id,
                BookingCode = row.BookingCode.Value,
                BuyerUserId = row.PassengerUserId,
                row.TripId,
                Status = row.Status.ToString(),
                row.TripSnapshotRouteName,
                row.TripSnapshotOriginName,
                row.TripSnapshotDestName,
                row.TripSnapshotDeparture,
                row.TripCurrentDeparture,
                SeatCount = row.Passengers.Count,
                BaseFare = row.BaseFare.Amount,
                DiscountAmount = row.DiscountAmount.Amount,
                TotalAmount = row.TotalAmount.Amount,
                row.PickupStationId,
                row.PickupStopId,
                row.DropoffStationId,
                row.DropoffStopId,
                row.BookingGroupId,
                TripDirection = row.TripDirection == null ? null : row.TripDirection.ToString(),
                CancellationReason = row.CancellationReason == null ? null : row.CancellationReason.ToString(),
                row.CreatedAt,
            }).SingleOrDefaultAsync(ct);
        if (booking is null)
            return null;

        var seats = await (from passenger in _db.Passengers.AsNoTracking()
                           join ticket in _db.Tickets.AsNoTracking() on passenger.Id equals ticket.PassengerId
                           where passenger.BookingId == bookingId
                           orderby passenger.SeatNumber, passenger.Id
                           select new OperatorBookingSeatDto(passenger.Id, ticket.Id, ticket.TicketCode.Value,
                               passenger.SeatNumber, ticket.Status.ToString(), passenger.BoardingStatus.ToString()))
            .ToListAsync(ct);
        var timeline = await _db.BookingStatusHistories.AsNoTracking()
            .Where(history => history.BookingId == bookingId)
            .OrderBy(history => history.OccurredAt).ThenBy(history => history.Id)
            .Select(history => new OperatorBookingStatusTimelineDto(history.Status.ToString(), history.OccurredAt, history.ReasonCode))
            .ToListAsync(ct);

        return new OperatorBookingDetailDto(booking.Id, booking.BookingCode, booking.BuyerUserId, booking.TripId,
            booking.Status, new OperatorBookingTripDto(booking.TripSnapshotRouteName, booking.TripSnapshotOriginName,
                booking.TripSnapshotDestName, booking.TripSnapshotDeparture, booking.TripCurrentDeparture),
            booking.SeatCount, booking.BaseFare,
            booking.DiscountAmount, booking.TotalAmount, booking.PickupStationId, booking.PickupStopId,
            booking.DropoffStationId, booking.DropoffStopId, booking.BookingGroupId, booking.TripDirection,
            booking.CancellationReason, booking.CreatedAt, seats, timeline);
    }

    public Task<bool> BookingExistsAsync(Guid bookingId, CancellationToken ct = default)
        => _db.Bookings.AsNoTracking().AnyAsync(row => row.Id == bookingId, ct);

    public async Task<OperatorBookingListPage> ListOperatorBookingsAsync(
        OperatorBookingListCriteria criteria,
        CancellationToken ct = default)
    {
        IQueryable<BookingEntity> query = string.IsNullOrEmpty(criteria.BookingCode)
            ? _db.Bookings.Where(booking => booking.OperatorId == criteria.OperatorId)
            : _db.Bookings.FromSqlInterpolated($@"
                SELECT *
                FROM vietride_booking.bookings
                WHERE operator_id = {criteria.OperatorId}
                  AND UPPER(booking_code) = UPPER({criteria.BookingCode})");
        query = query.AsNoTracking();

        if (criteria.Statuses is { Count: > 0 })
            query = query.Where(booking => criteria.Statuses.Contains(booking.Status));
        if (criteria.TripId.HasValue)
            query = query.Where(booking => booking.TripId == criteria.TripId.Value);
        if (criteria.DepartureFrom.HasValue)
            query = query.Where(booking => booking.TripCurrentDeparture >= criteria.DepartureFrom.Value);
        if (criteria.DepartureTo.HasValue)
            query = query.Where(booking => booking.TripCurrentDeparture < criteria.DepartureTo.Value);
        if (criteria.PassengerUserId.HasValue)
            query = query.Where(booking => booking.PassengerUserId == criteria.PassengerUserId.Value);
        var totalItems = await query.LongCountAsync(ct);

        var offset = ((long)criteria.Page - 1) * criteria.PageSize;
        if (offset >= totalItems)
            return new OperatorBookingListPage([], totalItems);

        // IQueryable.Skip accepts only an int. Do the paging arithmetic in long first and
        // narrow only after the count proves this is a real, representable page offset.
        if (offset > int.MaxValue)
            throw new InvalidOperationException("The requested page offset exceeds the EF paging limit.");
        var safeOffset = (int)offset;
        query = ApplyOrdering(query, criteria.SortBy, criteria.SortDescending);
        var items = await query
            .Skip(safeOffset)
            .Take(criteria.PageSize)
            .Select(booking => new OperatorBookingListItem(
                booking.Id,
                booking.BookingCode.Value,
                booking.TripId,
                booking.Status.ToString(),
                new OperatorBookingTripDto(
                    booking.TripSnapshotRouteName,
                    booking.TripSnapshotOriginName,
                    booking.TripSnapshotDestName,
                    booking.TripSnapshotDeparture,
                    booking.TripCurrentDeparture),
                booking.Passengers.Count,
                booking.TotalAmount.Amount,
                booking.CreatedAt))
            .ToListAsync(ct);

        return new OperatorBookingListPage(items, totalItems);
    }

    private static IQueryable<BookingEntity> ApplyOrdering(
        IQueryable<BookingEntity> query,
        string sortBy,
        bool descending)
        => (sortBy, descending) switch
        {
            ("departureAt", false) => query.OrderBy(x => x.TripCurrentDeparture).ThenBy(x => x.Id),
            ("departureAt", true) => query.OrderByDescending(x => x.TripCurrentDeparture).ThenByDescending(x => x.Id),
            ("bookingCode", false) => query.OrderBy(x => x.BookingCode).ThenBy(x => x.Id),
            ("bookingCode", true) => query.OrderByDescending(x => x.BookingCode).ThenByDescending(x => x.Id),
            ("status", false) => query.OrderBy(x => x.Status).ThenBy(x => x.Id),
            ("status", true) => query.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            ("totalAmount", false) => query.OrderBy(x => x.TotalAmount).ThenBy(x => x.Id),
            ("totalAmount", true) => query.OrderByDescending(x => x.TotalAmount).ThenByDescending(x => x.Id),
            ("createdAt", false) => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
        };

    public async Task<BookingEntity?> FindByBookingCodeAsync(
        string bookingCode,
        CancellationToken ct = default)
    {
        var code = BookingCode.Parse(bookingCode);

        // Compare the mapped value object. Its configured value converter translates the
        // constant to the booking_code column; EF.Property must use the model property name,
        // not the physical snake_case column name.
        return await _db.Bookings
            .FirstOrDefaultAsync(b => b.BookingCode == code, ct);
    }

    /// <inheritdoc/>
    public async Task<BookingEntity?> FindByTicketCodeWithPassengersAsync(
        string ticketCode,
        CancellationToken ct = default)
    {
        var code = TicketCode.Parse(ticketCode);

        return await _db.Bookings
            .Include(b => b.Passengers)
            .Include(b => b.Tickets)
            .FirstOrDefaultAsync(b => b.Tickets.Any(t => t.TicketCode == code), ct);
    }

    /// <inheritdoc/>
    public async Task<BookingEntity?> FindByIdAsync(
        Guid bookingId,
        CancellationToken ct = default)
        => await _db.Bookings
            .Include(b => b.ShuttleIntent)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

    public async Task<BookingEntity?> FindByIdForUpdateAsync(
        Guid bookingId,
        CancellationToken ct = default)
    {
        var trackedBooking = _db.Bookings.Local.SingleOrDefault(booking => booking.Id == bookingId);
        if (trackedBooking is not null)
            _db.Entry(trackedBooking).State = EntityState.Detached;

        var trackedIntent = _db.BookingShuttleIntents.Local.SingleOrDefault(intent => intent.BookingId == bookingId);
        if (trackedIntent is not null)
            _db.Entry(trackedIntent).State = EntityState.Detached;

        return await _db.Bookings
            .FromSqlInterpolated($"SELECT * FROM vietride_booking.bookings WHERE id = {bookingId} FOR UPDATE")
            .Include(booking => booking.Tickets)
            .Include(booking => booking.ShuttleIntent)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<BookingEntity>> GetNoShowCandidatesAsync(CancellationToken ct = default)
        => await _db.Bookings.AsNoTracking()
            .Where(booking => booking.Status == BookingStatus.CONFIRMED
                && booking.Passengers.Any(passenger => passenger.BoardingStatus == PassengerBoardingStatus.PENDING))
            .OrderBy(booking => booking.Id)
            .ToListAsync(ct);

    public async Task<BookingEntity?> FindConfirmedWithPassengersForUpdateAsync(
        Guid bookingId,
        CancellationToken ct = default)
    {
        foreach (var tracked in _db.ChangeTracker.Entries<BookingEntity>()
                     .Where(entry => entry.Entity.Id == bookingId).ToArray())
        {
            tracked.State = EntityState.Detached;
        }

        return await _db.Bookings
            .FromSqlInterpolated($"""
                SELECT * FROM vietride_booking.bookings
                WHERE id = {bookingId} AND status = 'CONFIRMED'
                FOR UPDATE
                """)
            .Include(booking => booking.Passengers)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<int> RelinkActiveStationReferencesAsync(
        IReadOnlyCollection<Guid> sourceStationIds,
        Guid canonicalStationId,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
    {
        var sourceIds = sourceStationIds.Distinct().ToArray();
        if (sourceIds.Length == 0)
            return 0;

        return await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_booking.bookings
            SET pickup_station_id = CASE
                    WHEN pickup_station_id = ANY({sourceIds}) THEN {canonicalStationId}
                    ELSE pickup_station_id
                END,
                dropoff_station_id = CASE
                    WHEN dropoff_station_id = ANY({sourceIds}) THEN {canonicalStationId}
                    ELSE dropoff_station_id
                END,
                updated_at = {updatedAt.ToUniversalTime()}
            WHERE status IN (
                    'PENDING_PAYMENT'::public.booking_status,
                    'CONFIRMED'::public.booking_status)
              AND (
                    pickup_station_id = ANY({sourceIds})
                    OR dropoff_station_id = ANY({sourceIds}))
            """, ct);
    }

    /// <inheritdoc/>
    public async Task<BookingEntity?> FindByIdWithPassengersAsync(
        Guid bookingId,
        CancellationToken ct = default)
        => await _db.Bookings
            .Include(b => b.Passengers)
            .Include(b => b.Tickets)
            .Include(b => b.ShuttleIntent)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

    /// <inheritdoc/>
    public async Task<BookingPaymentTransitionSnapshot?> GetPendingPaymentTransitionSnapshotAsync(
        Guid bookingId,
        CancellationToken ct = default)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.Id == bookingId && b.Status == BookingStatus.PENDING_PAYMENT)
            .Select(b => new
            {
                b.Id,
                b.PassengerUserId,
                b.TripId,
                b.SeatLockToken,
                TotalAmount = b.TotalAmount.Amount,
            })
            .FirstOrDefaultAsync(ct);
        if (booking is null)
        {
            return null;
        }

        var passengerSeatAssignments = await _db.Passengers
            .AsNoTracking()
            .Where(p => p.BookingId == bookingId)
            .OrderBy(p => p.SeatNumber)
            .Select(p => new
            {
                p.Id,
                p.SeatNumber,
            })
            .ToArrayAsync(ct);
        var assignedPassengers = passengerSeatAssignments
            .Select(p => new PassengerSeatAssignment(
                p.Id,
                p.SeatNumber
                    ?? throw new InvalidOperationException(
                        "A pending-payment passenger must have a seat number.")))
            .ToArray();

        var voucherUsageId = await _db.VoucherUsages
            .AsNoTracking()
            .Where(vu => vu.BookingId == bookingId)
            .Select(vu => (Guid?)vu.Id)
            .FirstOrDefaultAsync(ct);

        var ticketCodes = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.BookingId == bookingId)
            .OrderBy(t => t.SeatNumber)
            .Select(t => t.TicketCode.Value)
            .ToArrayAsync(ct);

        var ticketIds = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.BookingId == bookingId)
            .OrderBy(t => t.SeatNumber)
            .Select(t => t.Id)
            .ToArrayAsync(ct);
        var shuttleIntent = await _db.BookingShuttleIntents
            .AsNoTracking()
            .Where(intent => intent.BookingId == bookingId && intent.IsActive)
            .Select(intent => new BookingShuttleIntentSnapshot(
                intent.PickupAddress,
                intent.PickupLatitude,
                intent.PickupLongitude))
            .SingleOrDefaultAsync(ct);

        return new BookingPaymentTransitionSnapshot(
            booking.Id,
            booking.PassengerUserId,
            booking.TripId,
            booking.SeatLockToken,
            booking.TotalAmount,
            voucherUsageId,
            assignedPassengers,
            ticketCodes,
            ticketIds,
            shuttleIntent);
    }

    /// <inheritdoc/>
    public async Task<bool> TryConfirmPendingPaymentAsync(
        Guid bookingId,
        DateTimeOffset confirmedAt,
        CancellationToken ct = default)
    {
        var updated = await _db.Bookings
            .Where(b => b.Id == bookingId && b.Status == BookingStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(b => b.Status, BookingStatus.CONFIRMED)
                .SetProperty(b => b.ConfirmedAt, confirmedAt)
                .SetProperty(b => b.UpdatedAt, confirmedAt), ct);

        if (updated == 1)
        {
            await _db.Tickets
                .Where(t => t.BookingId == bookingId && t.Status == TicketStatus.PENDING_PAYMENT)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Status, TicketStatus.ISSUED)
                    .SetProperty(t => t.IssuedAt, confirmedAt)
                    .SetProperty(t => t.UpdatedAt, confirmedAt), ct);
        }

        return updated == 1;
    }

    /// <inheritdoc/>
    public async Task<bool> TryConfirmPendingPaymentGroupAsync(
        IReadOnlyCollection<Guid> bookingIds,
        DateTimeOffset confirmedAt,
        CancellationToken ct = default)
    {
        var ids = bookingIds.Distinct().Order().ToArray();
        if (ids.Length == 0)
        {
            return false;
        }

        var pendingStatus = BookingStatus.PENDING_PAYMENT.ToString();
        var confirmedStatus = BookingStatus.CONFIRMED.ToString();
        var updated = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_booking.bookings
            SET status = CAST({confirmedStatus} AS public.booking_status),
                confirmed_at = {confirmedAt},
                updated_at = {confirmedAt}
            WHERE id = ANY ({ids})
              AND status = CAST({pendingStatus} AS public.booking_status)
              AND (
                  SELECT COUNT(*)
                  FROM vietride_booking.bookings candidate
                  WHERE candidate.id = ANY ({ids})
                    AND candidate.status = CAST({pendingStatus} AS public.booking_status)
              ) = {ids.Length}
            """, ct).ConfigureAwait(false);
        if (updated != ids.Length)
        {
            return false;
        }

        await _db.Tickets
            .Where(ticket => ids.Contains(ticket.BookingId)
                && ticket.Status == TicketStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(ticket => ticket.Status, TicketStatus.ISSUED)
                .SetProperty(ticket => ticket.IssuedAt, confirmedAt)
                .SetProperty(ticket => ticket.UpdatedAt, confirmedAt), ct)
            .ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> TryExpirePendingPaymentAsync(
        Guid bookingId,
        DateTimeOffset expiredAt,
        CancellationToken ct = default)
    {
        var updated = await _db.Bookings
            .Where(b => b.Id == bookingId && b.Status == BookingStatus.PENDING_PAYMENT)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(b => b.Status, BookingStatus.EXPIRED)
                .SetProperty(b => b.ExpiredAt, expiredAt)
                .SetProperty(b => b.UpdatedAt, expiredAt), ct);

        if (updated == 1)
        {
            await _db.Tickets
                .Where(t => t.BookingId == bookingId && t.Status == TicketStatus.PENDING_PAYMENT)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Status, TicketStatus.EXPIRED)
                    .SetProperty(t => t.ExpiredAt, expiredAt)
                    .SetProperty(t => t.UpdatedAt, expiredAt), ct);
        }

        return updated == 1;
    }

    /// <inheritdoc/>
    public async Task<bool> TryCancelAsync(
        Guid bookingId,
        BookingCancellationReason reason,
        DateTimeOffset cancelledAt,
        bool refundOverride,
        CancellationToken ct = default)
    {
        var updated = await _db.Bookings
            .Where(b => b.Id == bookingId
                && (b.Status == BookingStatus.CONFIRMED || b.Status == BookingStatus.PENDING_PAYMENT))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(b => b.Status, BookingStatus.CANCELLED)
                .SetProperty(b => b.CancellationReason, reason)
                .SetProperty(b => b.CancelledAt, cancelledAt)
                .SetProperty(b => b.RefundOverride, refundOverride)
                .SetProperty(b => b.UpdatedAt, cancelledAt), ct);

        if (updated == 1)
        {
            await _db.BookingShuttleIntents
                .Where(intent => intent.BookingId == bookingId && intent.IsActive)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(intent => intent.IsActive, false)
                    .SetProperty(intent => intent.CancelledAt, cancelledAt)
                    .SetProperty(intent => intent.UpdatedAt, cancelledAt), ct);

            await _db.Tickets
                .Where(t => t.BookingId == bookingId
                    && (t.Status == TicketStatus.PENDING_PAYMENT || t.Status == TicketStatus.ISSUED))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Status, TicketStatus.CANCELLED)
                    .SetProperty(t => t.CancelledAt, cancelledAt)
                    .SetProperty(t => t.UpdatedAt, cancelledAt), ct);
        }

        return updated == 1;
    }

    /// <inheritdoc/>
    public async Task<bool> TryMarkCancelledRefundedAsync(
        Guid bookingId,
        DateTimeOffset refundedAt,
        CancellationToken ct = default)
    {
        var updated = await _db.Bookings
            .Where(b => b.Id == bookingId
                && (b.Status == BookingStatus.CANCELLED || b.Status == BookingStatus.DISRUPTED))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(b => b.Status, BookingStatus.REFUNDED)
                .SetProperty(b => b.RefundedAt, refundedAt)
                .SetProperty(b => b.UpdatedAt, refundedAt), ct);

        if (updated == 1)
        {
            await _db.Tickets
                .Where(t => t.BookingId == bookingId && t.Status == TicketStatus.CANCELLED)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Status, TicketStatus.REFUNDED)
                    .SetProperty(t => t.RefundedAt, refundedAt)
                    .SetProperty(t => t.UpdatedAt, refundedAt), ct);
        }

        return updated == 1;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> TryCompleteEligibleByTripIdAsync(
        Guid tripId,
        DateTimeOffset completedAt,
        CancellationToken ct = default)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE vietride_booking.bookings
            SET status = CAST(@target_status AS public.booking_status),
                completed_at = @completed_at
            WHERE trip_id = @trip_id
              AND status IN (
                  CAST(@confirmed_status AS public.booking_status),
                  CAST(@partial_no_show_status AS public.booking_status))
            RETURNING id;
            """;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        AddParameter(command, "target_status", BookingStatus.COMPLETED.ToString());
        AddParameter(command, "confirmed_status", BookingStatus.CONFIRMED.ToString());
        AddParameter(command, "partial_no_show_status", BookingStatus.PARTIAL_NO_SHOW.ToString());
        AddParameter(command, "trip_id", tripId);
        AddParameter(command, "completed_at", completedAt.ToUniversalTime());

        var transitionedBookingIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            transitionedBookingIds.Add(reader.GetGuid(0));
        }

        return transitionedBookingIds;
    }

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

    /// <inheritdoc/>
    public async Task<bool> HasConfirmedBookingAsync(Guid passengerUserId, CancellationToken ct = default)
        => await _db.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.PassengerUserId == passengerUserId && b.Status == BookingStatus.CONFIRMED, ct);
}
