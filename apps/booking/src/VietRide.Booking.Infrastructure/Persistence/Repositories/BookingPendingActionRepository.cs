using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

internal sealed class BookingPendingActionRepository(BookingDbContext db) : IBookingPendingActionRepository
{
    public Task<BookingPendingAction?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.BookingPendingActions.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<BookingPendingAction> AddAsync(BookingPendingAction entity, CancellationToken ct)
    {
        await db.BookingPendingActions.AddAsync(entity, ct);
        return entity;
    }
    public void Update(BookingPendingAction entity) => db.BookingPendingActions.Update(entity);
    public void Remove(BookingPendingAction entity) => db.BookingPendingActions.Remove(entity);
    public IQueryable<BookingPendingAction> Query() => db.BookingPendingActions;
    public IQueryable<BookingPendingAction> QueryNoTracking() => db.BookingPendingActions.AsNoTracking();
}
