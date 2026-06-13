namespace VietRide.Trip.Application.Features.Internal.Trips.Requests;

public sealed record LockSeatsRequest(
    IReadOnlyList<string> SeatNumbers,
    Guid HoldOwnerId,
    int? TtlSeconds);
