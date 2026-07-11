using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Repositories;
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

    public BookingRepository(BookingDbContext db)
    {
        _db = db;
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

    // -----------------------------------------------------------------------
    // IBookingRepository — aggregate-specific queries
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
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
            query = query.Where(booking => booking.TripSnapshotDeparture >= criteria.DepartureFrom.Value);
        if (criteria.DepartureTo.HasValue)
            query = query.Where(booking => booking.TripSnapshotDeparture < criteria.DepartureTo.Value);
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
                    booking.TripSnapshotDeparture),
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
            ("departureAt", false) => query.OrderBy(x => x.TripSnapshotDeparture).ThenBy(x => x.Id),
            ("departureAt", true) => query.OrderByDescending(x => x.TripSnapshotDeparture).ThenByDescending(x => x.Id),
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
        => await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);

    /// <inheritdoc/>
    public async Task<BookingEntity?> FindByIdWithPassengersAsync(
        Guid bookingId,
        CancellationToken ct = default)
        => await _db.Bookings
            .Include(b => b.Passengers)
            .Include(b => b.Tickets)
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
            .Select(p => new PassengerSeatAssignment(p.Id, p.SeatNumber))
            .ToArrayAsync(ct);

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

        return new BookingPaymentTransitionSnapshot(
            booking.Id,
            booking.PassengerUserId,
            booking.TripId,
            booking.SeatLockToken,
            booking.TotalAmount,
            voucherUsageId,
            passengerSeatAssignments,
            ticketCodes);
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
            .Where(b => b.Id == bookingId && b.Status == BookingStatus.CANCELLED)
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
    public async Task<bool> HasConfirmedBookingAsync(Guid passengerUserId, CancellationToken ct = default)
        => await _db.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.PassengerUserId == passengerUserId && b.Status == BookingStatus.CONFIRMED, ct);
}
