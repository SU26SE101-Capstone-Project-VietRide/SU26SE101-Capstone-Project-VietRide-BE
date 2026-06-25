using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
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
        // Compare the converted string primitive directly to avoid fragile EF translation
        // of struct equality through a value converter.
        return await _db.Bookings
            .FirstOrDefaultAsync(
                b => EF.Property<string>(b, "booking_code") == bookingCode,
                ct);
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

        return new BookingPaymentTransitionSnapshot(
            booking.Id,
            booking.PassengerUserId,
            booking.TripId,
            booking.SeatLockToken,
            booking.TotalAmount,
            voucherUsageId,
            passengerSeatAssignments);
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

        return updated == 1;
    }
}
