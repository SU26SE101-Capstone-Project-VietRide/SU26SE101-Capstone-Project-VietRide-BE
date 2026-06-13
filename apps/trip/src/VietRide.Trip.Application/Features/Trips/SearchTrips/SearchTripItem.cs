namespace VietRide.Trip.Application.Features.Trips.SearchTrips;

public sealed record SearchTripItem(
    Guid TripId,
    Guid OperatorId,
    string OperatorName,
    Guid RouteId,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset EstimatedArrivalTime,
    SearchTripStationDto OriginStation,
    SearchTripStationDto DestinationStation,
    int AvailableSeats,
    long BaseFare,
    bool AllowAlongRoutePickup,
    bool AllowAlongRouteDropoff);
