namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripStopDto(
    Guid StopId,
    int OrderIndex,
    bool AllowPickup,
    bool AllowDropoff,
    DateTimeOffset EstimatedArrivalTime,
    double DistanceFromOriginKm,
    long? FareFromThisStop);
