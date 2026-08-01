using VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;
using VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;
using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Booking.Application.Abstractions.Repositories;

/// <summary>
/// Repository contract for the BookingStats counter aggregate.
/// </summary>
public interface IBookingStatsRepository : IRepository<BookingStats, Guid>
{
    /// <summary>
    /// Claims a lifecycle event for BookingStats.
    /// Returns <c>false</c> when the event was already processed.
    /// </summary>
    Task<bool> TryClaimProcessedEventAsync(
        string eventType,
        Guid bookingId,
        DateTimeOffset processedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Applies additive counter deltas for the natural key
    /// <c>(operator_id, stat_date, COALESCE(trip_id, zero-uuid))</c>.
    /// </summary>
    Task UpsertDeltaAsync(BookingStats delta, CancellationToken ct = default);

    Task<IReadOnlyList<OperatorBookingStatsReadModel>> GetOperatorStatsAsync(
        Guid operatorId,
        DateOnly? from,
        DateOnly? to,
        string groupBy,
        CancellationToken ct = default);

    Task<IReadOnlyList<AdminBookingStatsAggregateReadModel>> GetAdminAggregateStatsAsync(
        DateOnly? from,
        DateOnly? to,
        string groupBy,
        CancellationToken ct = default);
}
