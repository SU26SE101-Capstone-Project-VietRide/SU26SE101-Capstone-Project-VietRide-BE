namespace VietRide.Trip.Application.Features.Trips.GetTripDetail;

public sealed record TripDetailDto(
    Guid TripId,
    Guid OperatorId,
    Guid RouteId,
    Guid VehicleId,
    string Status,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset EstimatedArrivalTime,
    DateTimeOffset? DestinationArrivedAt,
    long BaseFare,
    TripStationDto OriginStation,
    TripStationDto DestinationStation,
    IReadOnlyList<TripStopDto> Stops,
    TripSeatSummaryDto SeatSummary,
    Guid? ReturnRouteId,
    TripFareBreakdownDto FareBreakdown)
{
    public string? Notes { get; init; }
    public string PlannedEtaQuality { get; init; } = "FALLBACK";
    public int SurchargePercent { get; init; }
    public long SurchargeAmount { get; init; }
    public long EffectiveFare { get; init; } = BaseFare;
    public Guid? SurchargePeriodId { get; init; }
    public string? SurchargePeriodName { get; init; }
}
