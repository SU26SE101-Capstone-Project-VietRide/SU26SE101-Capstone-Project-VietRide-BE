namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public sealed record TripStopPendingPassengerCountProjection(
    Guid TripId,
    Guid StopId,
    int PendingPassengerCount);
