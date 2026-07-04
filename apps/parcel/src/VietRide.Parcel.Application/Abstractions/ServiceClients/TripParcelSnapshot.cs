namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripParcelSnapshot(
    Guid TripId,
    Guid OperatorId,
    Guid RouteId,
    Guid VehicleId,
    string Status,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset EstimatedArrivalTime,
    long BaseFare,
    TripStationDto OriginStation,
    TripStationDto DestinationStation,
    IReadOnlyList<TripStopDto> Stops,
    TripSeatSummaryDto SeatSummary,
    Guid? ReturnRouteId);
