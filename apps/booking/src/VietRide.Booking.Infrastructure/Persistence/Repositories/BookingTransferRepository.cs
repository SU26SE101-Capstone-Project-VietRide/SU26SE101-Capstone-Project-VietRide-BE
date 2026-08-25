using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

internal sealed class BookingTransferRepository(BookingDbContext db) : IBookingTransferRepository
{
    public Task<BookingTransfer?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.BookingTransfers.FirstOrDefaultAsync(transfer => transfer.Id == id, ct);

    public async Task<BookingTransfer> AddAsync(BookingTransfer entity, CancellationToken ct)
    {
        await db.BookingTransfers.AddAsync(entity, ct);
        return entity;
    }

    public void Update(BookingTransfer entity) => db.BookingTransfers.Update(entity);

    public void Remove(BookingTransfer entity) => db.BookingTransfers.Remove(entity);

    public IQueryable<BookingTransfer> Query() => db.BookingTransfers;

    public IQueryable<BookingTransfer> QueryNoTracking() => db.BookingTransfers.AsNoTracking();

    public async Task<BookingTransfer?> GetActiveForConfirmationAsync(
        Guid passengerId,
        Guid newTripId,
        Guid operatorId,
        CancellationToken ct = default)
    {
        var matches = await db.BookingTransfers
            .FromSqlInterpolated($"""
                SELECT booking_transfer.*
                FROM vietride_booking.booking_transfers AS booking_transfer
                INNER JOIN vietride_booking.bookings AS booking
                    ON booking.id = booking_transfer.booking_id
                WHERE booking_transfer.passenger_id = {passengerId}
                    AND booking_transfer.new_trip_id = {newTripId}
                    AND booking.trip_id = {newTripId}
                    AND booking.operator_id = {operatorId}
                    AND booking_transfer.confirmation_status
                        IN ('PENDING_CONFIRM', 'ESCALATED', 'CONFIRMED')
                ORDER BY booking_transfer.transferred_at DESC, booking_transfer.id DESC
                LIMIT 1
                FOR UPDATE OF booking, booking_transfer
                """)
            .ToListAsync(ct);

        return matches.SingleOrDefault();
    }

    public async Task<IReadOnlyList<BookingTransfer>> GetByPassengerTripPairAsync(
        IReadOnlyCollection<Guid> passengerIds,
        Guid originalTripId,
        Guid newTripId,
        CancellationToken ct = default)
        => await db.BookingTransfers
            .Where(transfer => passengerIds.Contains(transfer.PassengerId)
                && transfer.OriginalTripId == originalTripId
                && transfer.NewTripId == newTripId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BookingTransfer>> AcquirePendingEscalationBatchAsync(
        DateTimeOffset cutoff,
        int maxGroups,
        CancellationToken ct = default)
        => await db.BookingTransfers
            .FromSqlInterpolated($"""
                WITH candidate_groups AS (
                    SELECT booking_id, new_trip_id
                    FROM vietride_booking.booking_transfers
                    WHERE confirmation_status = 'PENDING_CONFIRM'
                        AND transferred_at < {cutoff}
                    GROUP BY booking_id, new_trip_id
                    ORDER BY min(transferred_at), booking_id, new_trip_id
                    LIMIT {maxGroups}
                ), locked_transfers AS (
                    SELECT booking_transfer.id
                    FROM vietride_booking.booking_transfers AS booking_transfer
                    INNER JOIN candidate_groups AS candidate
                        ON candidate.booking_id = booking_transfer.booking_id
                        AND candidate.new_trip_id = booking_transfer.new_trip_id
                    WHERE booking_transfer.confirmation_status = 'PENDING_CONFIRM'
                        AND booking_transfer.transferred_at < {cutoff}
                    ORDER BY booking_transfer.transferred_at, booking_transfer.id
                    FOR UPDATE OF booking_transfer SKIP LOCKED
                )
                SELECT booking_transfer.*
                FROM vietride_booking.booking_transfers AS booking_transfer
                INNER JOIN locked_transfers AS locked
                    ON locked.id = booking_transfer.id
                ORDER BY booking_transfer.transferred_at, booking_transfer.id
                """)
            .ToListAsync(ct);
}
