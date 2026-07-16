namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record ArriveTripDestinationResponse(
    Guid TripId,
    Guid DestinationStationId,
    string Status,
    DateTimeOffset ActualArrivalTime);
