namespace VietRide.Trip.Application.Abstractions.SeatLock;

/// <summary>
/// Coordinates short-lived seat holds for Booking's internal seat reservation flow.
/// </summary>
public interface ISeatLockStore
{
    /// <summary>
    /// Attempts to atomically acquire locks for all requested seats.
    /// </summary>
    Task<bool> TryAcquireAsync(
        Guid tripId,
        IReadOnlyCollection<string> seatNumbers,
        string lockOwner,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases locks owned by <paramref name="lockOwner" /> for the requested seats.
    /// </summary>
    Task ReleaseAsync(
        Guid tripId,
        IReadOnlyCollection<string> seatNumbers,
        string lockOwner,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the seat currently has a live Redis lock.
    /// </summary>
    Task<bool> IsLockedAsync(
        Guid tripId,
        string seatNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the live Redis lock exists and is owned by <paramref name="lockOwner" />.
    /// </summary>
    Task<bool> IsOwnedByAsync(
        Guid tripId,
        string seatNumber,
        string lockOwner,
        CancellationToken cancellationToken = default);
}
