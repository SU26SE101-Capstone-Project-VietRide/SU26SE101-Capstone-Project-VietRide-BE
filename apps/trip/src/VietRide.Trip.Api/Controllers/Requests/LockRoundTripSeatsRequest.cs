namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record LockRoundTripSeatsRequest(
    LockRoundTripSeatsLegRequest Outbound,
    LockRoundTripSeatsLegRequest Return,
    Guid HoldOwnerId,
    int? TtlSeconds);

public sealed record LockRoundTripSeatsLegRequest(Guid TripId, IReadOnlyList<string> SeatNumbers);
