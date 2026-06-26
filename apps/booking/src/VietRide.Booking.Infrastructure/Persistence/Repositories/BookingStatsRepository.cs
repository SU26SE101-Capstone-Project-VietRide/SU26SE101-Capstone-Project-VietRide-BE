using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for the BookingStats counter aggregate.
/// Implements <see cref="IBookingStatsRepository"/> - extends the generic repository contract
/// (<see cref="IRepository{TEntity,TId}"/>) with the natural-key UPSERT used by event consumers.
/// </summary>
internal sealed class BookingStatsRepository : IBookingStatsRepository
{
    private readonly BookingDbContext _db;

    public BookingStatsRepository(BookingDbContext db)
    {
        _db = db;
    }

    public async Task<BookingStats?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.BookingStats.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<BookingStats> AddAsync(BookingStats entity, CancellationToken ct)
    {
        await _db.BookingStats.AddAsync(entity, ct);
        return entity;
    }

    public void Update(BookingStats entity)
        => _db.BookingStats.Update(entity);

    public void Remove(BookingStats entity)
        => _db.BookingStats.Remove(entity);

    public IQueryable<BookingStats> Query()
        => _db.BookingStats;

    public IQueryable<BookingStats> QueryNoTracking()
        => _db.BookingStats.AsNoTracking();

    public async Task<bool> TryClaimProcessedEventAsync(
        string eventType,
        Guid bookingId,
        DateTimeOffset processedAt,
        CancellationToken ct = default)
    {
        var claimed = await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.booking_stats_processed_events (
    event_type,
    booking_id,
    processed_at
)
VALUES (
    {eventType},
    {bookingId},
    {processedAt}
)
ON CONFLICT (event_type, booking_id) DO NOTHING;", ct);

        return claimed == 1;
    }

    public async Task UpsertDeltaAsync(BookingStats delta, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.booking_stats (
    id,
    operator_id,
    operator_name,
    stat_date,
    trip_id,
    total_bookings,
    total_confirmed,
    total_cancelled,
    total_no_show,
    total_completed,
    total_revenue,
    total_refunded,
    total_seats_booked,
    updated_at
)
VALUES (
    {delta.Id},
    {delta.OperatorId},
    {delta.OperatorName},
    {delta.StatDate},
    {delta.TripId},
    {delta.TotalBookings},
    {delta.TotalConfirmed},
    {delta.TotalCancelled},
    {delta.TotalNoShow},
    {delta.TotalCompleted},
    {delta.TotalRevenue.Amount},
    {delta.TotalRefunded.Amount},
    {delta.TotalSeatsBooked},
    now()
)
ON CONFLICT (operator_id, stat_date, COALESCE(trip_id, '00000000-0000-0000-0000-000000000000'::uuid))
DO UPDATE SET
    operator_name = COALESCE(EXCLUDED.operator_name, booking_stats.operator_name),
    total_bookings = booking_stats.total_bookings + EXCLUDED.total_bookings,
    total_confirmed = booking_stats.total_confirmed + EXCLUDED.total_confirmed,
    total_cancelled = booking_stats.total_cancelled + EXCLUDED.total_cancelled,
    total_no_show = booking_stats.total_no_show + EXCLUDED.total_no_show,
    total_completed = booking_stats.total_completed + EXCLUDED.total_completed,
    total_revenue = booking_stats.total_revenue + EXCLUDED.total_revenue,
    total_refunded = booking_stats.total_refunded + EXCLUDED.total_refunded,
    total_seats_booked = booking_stats.total_seats_booked + EXCLUDED.total_seats_booked,
    updated_at = now();", ct);
    }
}
