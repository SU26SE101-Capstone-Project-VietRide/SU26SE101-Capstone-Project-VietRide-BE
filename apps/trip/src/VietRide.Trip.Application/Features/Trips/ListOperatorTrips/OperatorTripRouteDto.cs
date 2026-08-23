namespace VietRide.Trip.Application.Features.Trips.ListOperatorTrips;

public sealed record OperatorTripRouteDto(
    Guid RouteId,
    string Name,
    string OriginName,
    string DestinationName,
    string? Code = null);
