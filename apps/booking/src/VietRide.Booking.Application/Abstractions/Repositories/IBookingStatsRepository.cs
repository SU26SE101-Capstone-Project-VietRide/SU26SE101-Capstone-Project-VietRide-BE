using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Application.Abstractions.Repositories;

/// <summary>
/// Repository contract for the BookingStats counter aggregate.
/// </summary>
public interface IBookingStatsRepository : IRepository<BookingStats, Guid>
{
    /// <summary>
    /// Inserts or replaces the counter values for the natural key
    /// <c>(operator_id, stat_date, COALESCE(trip_id, zero-uuid))</c>.
    /// Replaying the same stats row is idempotent because counters are assigned, not incremented.
    /// </summary>
    Task UpsertAsync(BookingStats stats, CancellationToken ct = default);
}
