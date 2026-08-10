using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed record OperatorIncidentReadRow(
    Guid IncidentId,
    Guid TripId,
    IncidentCategory Category,
    string? Description,
    IReadOnlyCollection<string>? PhotoUrls,
    decimal? Latitude,
    decimal? Longitude,
    DateTimeOffset ReportedAt,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId,
    string? ResolutionNote,
    Guid ReportedByUserId,
    TripStatus TripStatus,
    DateTimeOffset DepartureDateTime,
    Guid RouteId,
    string RouteName,
    Guid OriginStationId,
    string OriginStationName,
    Guid DestinationStationId,
    string DestinationStationName);
