namespace VietRide.Trip.Application.Features.Internal.Trips.Requests;

public sealed record ReleaseSeatsRequest(
    Guid SeatLockToken,
    IReadOnlyList<string> SeatNumbers);
