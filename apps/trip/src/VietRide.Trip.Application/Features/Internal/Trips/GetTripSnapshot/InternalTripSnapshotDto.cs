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
    DateTimeOffset? ActualDepartureTime = null);
