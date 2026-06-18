using VietRide.Trip.Application.Features.Internal.Trips.LockSeats;

namespace VietRide.Trip.Application.Abstractions.SeatLock;

/// <summary>
/// Stores pending and successful lock-seats responses for short-lived Booking idempotency replay.
/// </summary>
public interface ISeatLockIdempotencyStore
{
    Task<SeatLockIdempotencyEntry?> GetAsync(
        Guid tripId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SeatLockIdempotencyReservation> TryReserveAsync(
        Guid tripId,
        string idempotencyKey,
        string requestFingerprint,
        IReadOnlyCollection<string> normalizedSeatNumbers,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<bool> StoreCompletedAsync(
        Guid tripId,
        string idempotencyKey,
        string requestFingerprint,
        string expectedReservationToken,
        IReadOnlyCollection<string> normalizedSeatNumbers,
        LockSeatsResult result,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task RemoveReservationAsync(
        Guid tripId,
        string idempotencyKey,
        string expectedReservationToken,
        CancellationToken cancellationToken = default);
}
