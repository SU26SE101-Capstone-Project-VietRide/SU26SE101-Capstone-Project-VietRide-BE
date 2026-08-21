namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripStopSummarySnapshot(
    Guid StopId,
    string Name,
    int OrderIndex,
    DateTimeOffset EstimatedArrivalAt,
    string Status,
    DateTimeOffset? ActualArrivalAt,
    DateTimeOffset? ActualDepartureAt);
