namespace VietRide.Trip.Application.Features.Internal.Trips.LockRoundTripSeats;

public sealed record LockRoundTripSeatsResult(
    LockRoundTripSeatsLegResult Outbound,
    LockRoundTripSeatsLegResult Return);

public sealed record LockRoundTripSeatsLegResult(
    Guid TripId,
    Guid SeatLockToken,
    IReadOnlyList<string> LockedSeats,
    DateTimeOffset ExpiresAt);
