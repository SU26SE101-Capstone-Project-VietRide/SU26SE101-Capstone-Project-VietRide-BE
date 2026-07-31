namespace VietRide.Trip.Application.Features.Trips.ListOperatorTrips;

public sealed record OperatorTripCrewDto(
    Guid UserId,
    string DisplayName,
    string? Phone);
