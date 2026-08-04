namespace VietRide.Trip.Application.Features.Trips.GetTripDetail;

public sealed record TripFareStopDto(Guid StopId, long FareFromThisStop)
{
    public int SurchargePercent { get; init; }
    public long SurchargeAmount { get; init; }
    public long EffectiveFareFromThisStop { get; init; } = FareFromThisStop;
}
