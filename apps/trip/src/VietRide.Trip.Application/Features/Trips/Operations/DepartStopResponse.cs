namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record DepartStopResponse(
    Guid TripId,
    Guid StopId,
    DateTimeOffset DepartedAt,
    int PendingPassengerCount,
    bool EventEmitted);
