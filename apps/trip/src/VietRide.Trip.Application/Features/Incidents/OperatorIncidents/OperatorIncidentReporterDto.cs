namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed record OperatorIncidentReporterDto(Guid UserId, string? DisplayName, string? Role);
