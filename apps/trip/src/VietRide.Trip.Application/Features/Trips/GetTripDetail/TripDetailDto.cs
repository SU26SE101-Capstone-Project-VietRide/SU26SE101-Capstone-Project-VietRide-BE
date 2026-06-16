namespace VietRide.Trip.Application.Features.Trips.GetTripDetail;

public sealed record TripDetailDto(
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
    Guid? ReturnRouteId,
    TripFareBreakdownDto FareBreakdown);
