namespace VietRide.Trip.Application.Features.Incidents.ReportIncident;

public sealed record ReportIncidentResponse(
    Guid IncidentId,
    Guid TripId,
    Guid ReportedByUserId,
    string Category,
    string? Description,
    IReadOnlyCollection<string>? PhotoUrls,
    decimal? Latitude,
    decimal? Longitude,
    DateTimeOffset ReportedAt);
