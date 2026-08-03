namespace VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;

public sealed record InternalTripSnapshotDto(
    Guid TripId,
    Guid OperatorId,
    Guid RouteId,
    Guid VehicleId,
    string Status,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset EstimatedArrivalTime,
    long BaseFare,
    InternalTripStationSnapshotDto OriginStation,
    InternalTripStationSnapshotDto DestinationStation,
    IReadOnlyList<InternalTripStopSnapshotDto> Stops,
    InternalTripSeatSummaryDto SeatSummary,
    Guid? ReturnRouteId,
    Guid? DriverUserId,
    Guid? AssistantUserId,
    DateTimeOffset? DestinationArrivedAt = null,
    DateTimeOffset? ActualDepartureTime = null,
    double? TotalDistanceKm = null)
{
    public long OriginalBaseFare { get; init; } = BaseFare;
    public int SurchargePercent { get; init; }
    public long SurchargeAmount { get; init; }
    public Guid? SurchargePeriodId { get; init; }
    public string? SurchargePeriodName { get; init; }
}
