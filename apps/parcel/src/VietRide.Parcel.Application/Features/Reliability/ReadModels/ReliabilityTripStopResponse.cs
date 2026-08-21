namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityTripStopResponse(
    Guid StopId,
    string Name,
    int OrderIndex,
    DateTimeOffset EstimatedArrivalAt,
    string Status,
    DateTimeOffset? ActualArrivalAt,
    DateTimeOffset? ActualDepartureAt);
