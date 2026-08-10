namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed record OperatorIncidentRouteDto(
    Guid RouteId,
    string Name,
    OperatorIncidentStationDto OriginStation,
    OperatorIncidentStationDto DestinationStation);
