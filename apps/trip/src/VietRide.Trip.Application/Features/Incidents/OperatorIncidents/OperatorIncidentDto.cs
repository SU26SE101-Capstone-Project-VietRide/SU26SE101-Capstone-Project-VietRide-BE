namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed record OperatorIncidentDto(
    Guid IncidentId,
    string Category,
    string? Description,
    IReadOnlyCollection<string>? PhotoUrls,
    decimal? Latitude,
    decimal? Longitude,
    DateTimeOffset ReportedAt,
    string Status,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId,
    string? ResolutionNote,
    OperatorIncidentTripDto Trip,
    OperatorIncidentReporterDto Reporter);
