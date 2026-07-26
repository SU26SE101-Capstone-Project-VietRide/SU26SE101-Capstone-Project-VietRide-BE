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
                        IN ('PENDING_CONFIRM', 'CONFIRMED')
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
}
