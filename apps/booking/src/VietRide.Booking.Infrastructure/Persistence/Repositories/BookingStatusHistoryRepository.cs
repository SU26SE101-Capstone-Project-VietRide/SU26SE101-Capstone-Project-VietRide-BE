using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

internal sealed class BookingStatusHistoryRepository : IBookingStatusHistoryRepository
{
    private readonly BookingDbContext _db;

    public BookingStatusHistoryRepository(BookingDbContext db) => _db = db;

    public async Task AddAsync(BookingStatusHistory history, CancellationToken ct = default)
        => await _db.BookingStatusHistories.AddAsync(history, ct);

    public IQueryable<BookingStatusHistory> QueryNoTracking()
        => _db.BookingStatusHistories.AsNoTracking()
            .OrderBy(history => history.BookingId)
            .ThenBy(history => history.OccurredAt)
            .ThenBy(history => history.Id);
}
