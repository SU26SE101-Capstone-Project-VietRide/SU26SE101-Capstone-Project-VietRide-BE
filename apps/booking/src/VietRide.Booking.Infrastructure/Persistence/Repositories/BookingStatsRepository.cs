using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;
using VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;
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
    total_no_show_passengers,
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
    {delta.TotalNoShowPassengers},
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
    total_no_show_passengers = booking_stats.total_no_show_passengers + EXCLUDED.total_no_show_passengers,
    total_completed = booking_stats.total_completed + EXCLUDED.total_completed,
    total_revenue = booking_stats.total_revenue + EXCLUDED.total_revenue,
    total_refunded = booking_stats.total_refunded + EXCLUDED.total_refunded,
    total_seats_booked = booking_stats.total_seats_booked + EXCLUDED.total_seats_booked,
    updated_at = now();", ct);
    }

    public async Task<IReadOnlyList<OperatorBookingStatsReadModel>> GetOperatorStatsAsync(
        Guid operatorId,
        DateOnly? from,
        DateOnly? to,
        string groupBy,
        CancellationToken ct = default)
    {
        if (string.Equals(groupBy, "month", StringComparison.OrdinalIgnoreCase))
        {
            var monthRows = await _db.Database.SqlQuery<OperatorBookingStatsSqlRow>($@"
SELECT
    operator_id AS ""OperatorId"",
    date_trunc('month', stat_date::timestamp)::date AS ""Date"",
    COALESCE(SUM(total_bookings), 0)::integer AS ""TotalBookings"",
    COALESCE(SUM(total_revenue), 0)::bigint AS ""TotalRevenue"",
    COALESCE(SUM(total_cancelled), 0)::integer AS ""TotalCancellations"",
    COALESCE(SUM(total_no_show), 0)::integer AS ""TotalNoShows"",
    COALESCE(SUM(total_no_show_passengers), 0)::integer AS ""NoShowPassengerCount"",
    COALESCE(SUM(total_completed), 0)::integer AS ""TotalCompleted""
FROM vietride_booking.booking_stats
WHERE operator_id = {operatorId}
  AND stat_date >= {from}::date
  AND stat_date <= {to}::date
GROUP BY operator_id, date_trunc('month', stat_date::timestamp)::date
ORDER BY date_trunc('month', stat_date::timestamp)::date")
                .ToListAsync(ct);

            return monthRows.Select(ToOperatorReadModel).ToList();
        }

        var rows = await _db.Database.SqlQuery<OperatorBookingStatsSqlRow>($@"
SELECT
    operator_id AS ""OperatorId"",
    stat_date AS ""Date"",
    COALESCE(SUM(total_bookings), 0)::integer AS ""TotalBookings"",
    COALESCE(SUM(total_revenue), 0)::bigint AS ""TotalRevenue"",
    COALESCE(SUM(total_cancelled), 0)::integer AS ""TotalCancellations"",
    COALESCE(SUM(total_no_show), 0)::integer AS ""TotalNoShows"",
    COALESCE(SUM(total_no_show_passengers), 0)::integer AS ""NoShowPassengerCount"",
    COALESCE(SUM(total_completed), 0)::integer AS ""TotalCompleted""
FROM vietride_booking.booking_stats
WHERE operator_id = {operatorId}
  AND ({from}::date IS NULL OR stat_date >= {from}::date)
  AND ({to}::date IS NULL OR stat_date <= {to}::date)
GROUP BY operator_id, stat_date
ORDER BY stat_date")
            .ToListAsync(ct);

        return rows.Select(ToOperatorReadModel).ToList();
    }

    public async Task<IReadOnlyList<AdminBookingStatsAggregateReadModel>> GetAdminAggregateStatsAsync(
        DateOnly? from,
        DateOnly? to,
        string groupBy,
        CancellationToken ct = default)
    {
        if (string.Equals(groupBy, "month", StringComparison.OrdinalIgnoreCase))
        {
            var monthRows = await _db.Database.SqlQuery<AdminBookingStatsAggregateSqlRow>($@"
SELECT
    NULL::uuid AS ""OperatorId"",
    NULL::text AS ""OperatorName"",
    date_trunc('month', stat_date::timestamp)::date AS ""Date"",
    COALESCE(SUM(total_bookings), 0)::integer AS ""TotalBookings"",
    COALESCE(SUM(total_revenue), 0)::bigint AS ""TotalRevenue"",
    COALESCE(SUM(total_cancelled), 0)::integer AS ""TotalCancellations"",
    COALESCE(SUM(total_no_show), 0)::integer AS ""TotalNoShows"",
    COALESCE(SUM(total_no_show_passengers), 0)::integer AS ""NoShowPassengerCount"",
    COALESCE(SUM(total_completed), 0)::integer AS ""TotalCompleted""
FROM vietride_booking.booking_stats
WHERE stat_date >= {from}::date
  AND stat_date <= {to}::date
GROUP BY date_trunc('month', stat_date::timestamp)::date
ORDER BY date_trunc('month', stat_date::timestamp)::date")
                .ToListAsync(ct);

            return monthRows.Select(ToAdminReadModel).ToList();
        }

        if (string.Equals(groupBy, "date", StringComparison.OrdinalIgnoreCase))
        {
            var rows = await _db.Database.SqlQuery<AdminBookingStatsAggregateSqlRow>($@"
WITH filtered AS (
    SELECT *
    FROM vietride_booking.booking_stats
    WHERE ({from}::date IS NULL OR stat_date >= {from}::date)
      AND ({to}::date IS NULL OR stat_date <= {to}::date)
),
names AS (
    SELECT DISTINCT ON (operator_id, stat_date)
        operator_id,
        stat_date,
        operator_name
    FROM filtered
    WHERE operator_name IS NOT NULL
    ORDER BY operator_id, stat_date, updated_at DESC, operator_name
)
SELECT
    filtered.operator_id AS ""OperatorId"",
    COALESCE(names.operator_name, '') AS ""OperatorName"",
    filtered.stat_date AS ""Date"",
    COALESCE(SUM(filtered.total_bookings), 0)::integer AS ""TotalBookings"",
    COALESCE(SUM(filtered.total_revenue), 0)::bigint AS ""TotalRevenue"",
    COALESCE(SUM(filtered.total_cancelled), 0)::integer AS ""TotalCancellations"",
    COALESCE(SUM(filtered.total_no_show), 0)::integer AS ""TotalNoShows"",
    COALESCE(SUM(filtered.total_no_show_passengers), 0)::integer AS ""NoShowPassengerCount"",
    COALESCE(SUM(filtered.total_completed), 0)::integer AS ""TotalCompleted""
FROM filtered
LEFT JOIN names
    ON names.operator_id = filtered.operator_id
   AND names.stat_date = filtered.stat_date
GROUP BY filtered.operator_id, filtered.stat_date, names.operator_name
ORDER BY filtered.stat_date, COALESCE(names.operator_name, '')")
                .ToListAsync(ct);

            return rows.Select(ToAdminReadModel).ToList();
        }

        var operatorRows = await _db.Database.SqlQuery<AdminBookingStatsAggregateSqlRow>($@"
WITH filtered AS (
    SELECT *
    FROM vietride_booking.booking_stats
    WHERE ({from}::date IS NULL OR stat_date >= {from}::date)
      AND ({to}::date IS NULL OR stat_date <= {to}::date)
),
names AS (
    SELECT DISTINCT ON (operator_id)
        operator_id,
        operator_name
    FROM filtered
    WHERE operator_name IS NOT NULL
    ORDER BY operator_id, updated_at DESC, operator_name
)
SELECT
    filtered.operator_id AS ""OperatorId"",
    COALESCE(names.operator_name, '') AS ""OperatorName"",
    NULL::date AS ""Date"",
    COALESCE(SUM(filtered.total_bookings), 0)::integer AS ""TotalBookings"",
    COALESCE(SUM(filtered.total_revenue), 0)::bigint AS ""TotalRevenue"",
    COALESCE(SUM(filtered.total_cancelled), 0)::integer AS ""TotalCancellations"",
    COALESCE(SUM(filtered.total_no_show), 0)::integer AS ""TotalNoShows"",
    COALESCE(SUM(filtered.total_no_show_passengers), 0)::integer AS ""NoShowPassengerCount"",
    COALESCE(SUM(filtered.total_completed), 0)::integer AS ""TotalCompleted""
FROM filtered
LEFT JOIN names
    ON names.operator_id = filtered.operator_id
GROUP BY filtered.operator_id, names.operator_name
ORDER BY COALESCE(names.operator_name, '')")
            .ToListAsync(ct);

        return operatorRows.Select(ToAdminReadModel).ToList();
    }

    private static AdminBookingStatsAggregateReadModel ToAdminReadModel(
        AdminBookingStatsAggregateSqlRow row)
        => new(
            row.OperatorId,
            row.OperatorName,
            row.Date,
            row.TotalBookings,
            row.TotalRevenue,
            row.TotalCancellations,
            row.TotalNoShows,
            row.TotalCompleted,
            row.NoShowPassengerCount);

    private static OperatorBookingStatsReadModel ToOperatorReadModel(OperatorBookingStatsSqlRow row)
        => new(
            row.OperatorId,
            row.Date,
            row.TotalBookings,
            row.TotalRevenue,
            row.TotalCancellations,
            row.TotalNoShows,
            row.TotalCompleted,
            row.NoShowPassengerCount);

    private sealed class OperatorBookingStatsSqlRow
    {
        public Guid OperatorId { get; set; }
        public DateOnly Date { get; set; }
        public int TotalBookings { get; set; }
        public long TotalRevenue { get; set; }
        public int TotalCancellations { get; set; }
        public int TotalNoShows { get; set; }
        public int NoShowPassengerCount { get; set; }
        public int TotalCompleted { get; set; }
    }

    private sealed class AdminBookingStatsAggregateSqlRow
    {
        public Guid? OperatorId { get; set; }
        public string? OperatorName { get; set; }
        public DateOnly? Date { get; set; }
        public int TotalBookings { get; set; }
        public long TotalRevenue { get; set; }
        public int TotalCancellations { get; set; }
        public int TotalNoShows { get; set; }
        public int NoShowPassengerCount { get; set; }
        public int TotalCompleted { get; set; }
    }
}
