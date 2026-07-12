using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Application.Abstractions.Repositories;

public interface IBookingStatusHistoryRepository
{
    Task AddAsync(BookingStatusHistory history, CancellationToken ct = default);
    IQueryable<BookingStatusHistory> QueryNoTracking();
}
