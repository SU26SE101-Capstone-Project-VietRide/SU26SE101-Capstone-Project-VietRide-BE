using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
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
            passengerSeatAssignments,
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
