namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed record OperatorIncidentTripDto(
    Guid TripId,
    string Status,
    DateTimeOffset DepartureDateTime,
    OperatorIncidentRouteDto Route);
