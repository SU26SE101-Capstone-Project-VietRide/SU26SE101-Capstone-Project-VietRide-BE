namespace VietRide.Trip.Application.Features.Trips.GetTripDetail;

public sealed record TripFareBreakdownDto(
    long BaseFare,
    IReadOnlyList<TripFareStopDto> Stops)
{
    public int SurchargePercent { get; init; }
    public long SurchargeAmount { get; init; }
    public long EffectiveBaseFare { get; init; } = BaseFare;
    public Guid? SurchargePeriodId { get; init; }
    public string? SurchargePeriodName { get; init; }
}
