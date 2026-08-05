namespace VietRide.Identity.Infrastructure.Messaging;

internal static class StationAuditEventValidation
{
    public static void Validate(StationMergedIntegrationEvent integrationEvent)
    {
        ValidateCommon(
            integrationEvent.EventId,
            integrationEvent.OccurredAt,
            integrationEvent.ActorUserId,
            integrationEvent.IpAddress,
            integrationEvent.UserAgent);
        if (integrationEvent.PrimaryStationId == Guid.Empty
            || integrationEvent.DuplicateStationId == Guid.Empty
            || integrationEvent.PrimaryStationId == integrationEvent.DuplicateStationId)
        {
            throw new InvalidOperationException("Station merge audit event contains invalid Station ids.");
        }

        ValidateSnapshot(integrationEvent.PrimaryBefore, integrationEvent.PrimaryStationId);
        ValidateSnapshot(integrationEvent.DuplicateBefore, integrationEvent.DuplicateStationId);
        ValidateSnapshot(integrationEvent.PrimaryAfter, integrationEvent.PrimaryStationId);
        var counts = integrationEvent.RelinkedCounts
            ?? throw new InvalidOperationException("Station merge audit event relinked counts are required.");
        if (counts.OperatorMappings < 0
            || counts.CollapsedOperatorMappings < 0
            || counts.RouteOrigins < 0
            || counts.RouteDestinations < 0
            || counts.AlternativeRoutes < 0
            || counts.ShuttleTrips < 0
            || counts.FlattenedRedirects < 0)
        {
            throw new InvalidOperationException("Station merge audit event contains negative relinked counts.");
        }
    }

    public static void Validate(StationNormalizedIntegrationEvent integrationEvent)
    {
        ValidateCommon(
            integrationEvent.EventId,
            integrationEvent.OccurredAt,
            integrationEvent.ActorUserId,
            integrationEvent.IpAddress,
            integrationEvent.UserAgent);
        if (integrationEvent.StationId == Guid.Empty)
            throw new InvalidOperationException("Station normalized audit event Station id is required.");

        ValidateSnapshot(integrationEvent.Before, integrationEvent.StationId);
        ValidateSnapshot(integrationEvent.After, integrationEvent.StationId);
    }

    private static void ValidateCommon(
        Guid eventId,
        DateTime occurredAt,
        Guid actorUserId,
        string? ipAddress,
        string? userAgent)
    {
        if (eventId == Guid.Empty)
            throw new InvalidOperationException("Station audit event id is required.");
        if (occurredAt == default || occurredAt.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Station audit event occurredAt must be a UTC instant.");
        if (actorUserId == Guid.Empty)
            throw new InvalidOperationException("Station audit event actor is required.");
        if (ipAddress is { Length: > 45 })
            throw new InvalidOperationException("Station audit event IP address is too long.");
        if (userAgent is { Length: > 500 })
            throw new InvalidOperationException("Station audit event user agent is too long.");
    }

    private static void ValidateSnapshot(StationAuditSnapshot? snapshot, Guid expectedStationId)
    {
        if (snapshot is null
            || snapshot.Id != expectedStationId
            || string.IsNullOrWhiteSpace(snapshot.Name)
            || string.IsNullOrWhiteSpace(snapshot.Slug)
            || string.IsNullOrWhiteSpace(snapshot.City))
        {
            throw new InvalidOperationException("Station audit event contains an invalid Station snapshot.");
        }

        if (snapshot.Latitude.HasValue != snapshot.Longitude.HasValue
            || snapshot.Latitude is < -90 or > 90
            || snapshot.Longitude is < -180 or > 180)
        {
            throw new InvalidOperationException("Station audit event contains invalid Station coordinates.");
        }
    }
}
