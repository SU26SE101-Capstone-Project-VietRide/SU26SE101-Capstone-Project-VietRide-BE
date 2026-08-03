namespace VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;

public sealed record InternalTripStopSnapshotDto(
    Guid StopId,
    int OrderIndex,
    bool AllowPickup,
    bool AllowDropoff,
    DateTimeOffset EstimatedArrivalTime,
    double? DistanceFromOriginKm,
    long? FareFromThisStop,
    string Status,
    DateTimeOffset? ActualArrivalTime,
    bool IsActive = true)
{
    public long? OriginalFareFromThisStop { get; init; }
    public int SurchargePercent { get; init; }
    public long SurchargeAmount { get; init; }
}
