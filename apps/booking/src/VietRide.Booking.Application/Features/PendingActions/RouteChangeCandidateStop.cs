namespace VietRide.Booking.Application.Features.PendingActions;

public sealed record RouteChangeCandidateStop(
    Guid? StopId,
    Guid? StationId,
    string StationName,
    int Sequence,
    DateTimeOffset EstimatedArrivalAt);
