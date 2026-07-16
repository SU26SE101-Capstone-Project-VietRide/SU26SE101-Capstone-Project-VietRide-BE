using MediatR;

namespace VietRide.Trip.Application.Features.Incidents.ReportIncident;

public sealed record ReportIncidentCommand(
    Guid TripId,
    Guid ReporterUserId,
    string Category,
    string? Description,
    IReadOnlyCollection<string>? PhotoUrls,
    decimal? Latitude,
    decimal? Longitude) : IRequest<ReportIncidentResponse>;
