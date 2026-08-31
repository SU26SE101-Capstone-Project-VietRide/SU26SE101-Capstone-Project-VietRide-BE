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
    string Status,
    DateTimeOffset EstimatedArrivalTime,
    DateTimeOffset? ActualArrivalTime,
    DateTimeOffset? ActualDepartureTime,
    double? DistanceFromOriginKm,
    long? FareFromThisStop,
    long EffectiveFare)
{
    public int SurchargePercent { get; init; }
    public long SurchargeAmount { get; init; }
    public Guid? SurchargePeriodId { get; init; }
    public string? SurchargePeriodName { get; init; }
}
