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
}
