namespace VietRide.Trip.Application.Features.Internal.Trips.LockSeats;

public sealed record LockSeatsResult(
    Guid SeatLockToken,
    IReadOnlyList<string> LockedSeats,
    DateTimeOffset ExpiresAt);
