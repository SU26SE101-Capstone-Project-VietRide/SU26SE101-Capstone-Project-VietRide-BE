using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

internal static class OperatorIncidentMapper
{
    public static OperatorIncidentDto ToDto(
        OperatorIncidentReadRow row,
        IReadOnlyDictionary<Guid, IdentityUserProfile> profiles)
    {
        profiles.TryGetValue(row.ReportedByUserId, out var reporter);
        return new OperatorIncidentDto(
            row.IncidentId,
            row.Category.ToString(),
            row.Description,
            row.PhotoUrls,
            row.Latitude,
            row.Longitude,
            row.ReportedAt,
            row.ResolvedAt.HasValue ? "RESOLVED" : "OPEN",
            row.ResolvedAt,
            row.ResolvedByUserId,
            row.ResolutionNote,
            new OperatorIncidentTripDto(
                row.TripId,
                row.TripStatus.ToString(),
                row.DepartureDateTime,
                new OperatorIncidentRouteDto(
                    row.RouteId,
                    row.RouteName,
                    new OperatorIncidentStationDto(row.OriginStationId, row.OriginStationName),
                    new OperatorIncidentStationDto(row.DestinationStationId, row.DestinationStationName))),
            new OperatorIncidentReporterDto(
                row.ReportedByUserId,
                reporter?.DisplayName,
                reporter?.Role));
    }
}
