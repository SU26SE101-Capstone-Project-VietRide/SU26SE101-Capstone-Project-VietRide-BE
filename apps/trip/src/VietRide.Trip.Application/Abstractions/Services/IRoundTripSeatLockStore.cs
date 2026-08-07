namespace VietRide.Trip.Application.Abstractions.Services;

public interface IRoundTripSeatLockStore
{
    Task<RoundTripSeatLockStoreResult> TryLockAsync(
        RoundTripSeatLockStoreRequest request,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        IReadOnlyList<RoundTripSeatLockKey> keys,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed record RoundTripSeatLockStoreRequest(
    RoundTripSeatLockLeg Outbound,
    RoundTripSeatLockLeg Return,
    Guid HoldOwnerId,
    string IdempotencyKey,
    TimeSpan Ttl);

public sealed record RoundTripSeatLockLeg(
    Guid TripId,
    IReadOnlyList<string> SeatNumbers,
    Guid SeatLockToken);

public sealed record RoundTripSeatLockStoreResult(
    bool IsReplay,
    bool Succeeded,
    IReadOnlyList<RoundTripSeatConflict> UnavailableSeats,
    RoundTripSeatLockReplay? Replay);

public sealed record RoundTripSeatConflict(string Field, string SeatNumber);

public sealed record RoundTripSeatLockReplay(
    RoundTripSeatLockReplayLeg Outbound,
    RoundTripSeatLockReplayLeg Return);

public sealed record RoundTripSeatLockReplayLeg(
    Guid TripId,
    Guid SeatLockToken,
    IReadOnlyList<string> LockedSeats,
    DateTimeOffset ExpiresAt);

public sealed record RoundTripSeatLockKey(Guid TripId, string SeatNumber);
