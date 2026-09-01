namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripStopDto(
    Guid StopId,
    int OrderIndex,
    bool AllowPickup,
    bool AllowDropoff,
    DateTimeOffset EstimatedArrivalTime,
    double? DistanceFromOriginKm,
    long? FareFromThisStop,
    string Status = "PENDING",
    DateTimeOffset? ActualArrivalTime = null,
    DateTimeOffset? ActualDepartureTime = null,
    bool IsActive = true,
    string? Name = null);
