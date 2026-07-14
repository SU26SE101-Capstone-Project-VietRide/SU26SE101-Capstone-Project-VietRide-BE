namespace VietRide.Trip.Application.Features.DriverTrips.StartTrip;

public sealed record StartTripResponse(
    Guid TripId,
    string Status,
    DateTimeOffset ActualDepartureTime);
