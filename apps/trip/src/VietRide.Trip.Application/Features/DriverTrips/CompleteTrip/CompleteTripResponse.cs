namespace VietRide.Trip.Application.Features.DriverTrips.CompleteTrip;

public sealed record CompleteTripResponse(
    Guid TripId,
    string Status,
    DateTimeOffset CompletedAt,
    Guid CompletedByUserId);
