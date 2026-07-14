namespace VietRide.Trip.Application.Features.Trips.GetTripDetail;

public sealed record TripStopDto(
    Guid StopId,
    string Name,
    string? Address,
    decimal Latitude,
    decimal Longitude,
    bool IsActive,
    int OrderIndex,
    bool AllowPickup,
    bool AllowDropoff,
    DateTimeOffset EstimatedArrivalTime,
    double? DistanceFromOriginKm,
    long? FareFromThisStop,
    long EffectiveFare);
