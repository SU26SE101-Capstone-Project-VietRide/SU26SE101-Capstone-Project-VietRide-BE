namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record ArriveTripStopResponse(
    Guid TripId,
    Guid StopId,
    string Status,
    DateTimeOffset ActualArrivalTime);
