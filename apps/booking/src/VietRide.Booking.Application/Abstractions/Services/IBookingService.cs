namespace VietRide.Booking.Application.Abstractions.Services;

/// <summary>
/// Orchestration service for cross-handler Booking business logic.
/// <para>
/// Per BSOT §3.2.5/§3.2.6 line 686: this interface is mandatory because
/// <see cref="ReleaseSeatsAsync"/> is shared by ≥2 handlers
/// (CreateBookingCommandHandler and the future CancelBookingCommandHandler on Day 17).
/// </para>
/// </summary>
public interface IBookingService
{
    /// <summary>
    /// Calls the Trip service to release held seats (compensation / cancel path).
    /// Idempotent: releasing an expired or already-released lock is a no-op.
    /// This lives here so Day-17 cancel/refund reuses it without duplicating the
    /// ITripServiceClient call.
    /// </summary>
    /// <param name="tripId">
    /// The trip whose seats should be released. Passed explicitly so that callers
    /// on the entity-creation-failure path (where no Booking entity exists yet)
    /// do not fall back to Guid.Empty — which would silently leak seats (S2 fix).
    /// </param>
    Task ReleaseSeatsAsync(
        Guid tripId,
        Guid seatLockToken,
        IReadOnlyList<string> seatNumbers,
        CancellationToken ct = default);
}
