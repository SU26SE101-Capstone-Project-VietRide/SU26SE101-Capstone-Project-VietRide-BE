using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Features.Incidents.ReportIncident;

public sealed class IncidentReportedIntegrationEvent : IntegrationEventBase
{
    public IncidentReportedIntegrationEvent(
        Guid incidentId,
        Guid tripId,
        Guid operatorId,
        Guid reporterUserId,
        string category,
        string? description,
        IReadOnlyCollection<string>? photoUrls,
        decimal? latitude,
        decimal? longitude,
        DateTimeOffset reportedAt)
        : base(Guid.NewGuid(), reportedAt.UtcDateTime)
    {
        IncidentId = incidentId;
        TripId = tripId;
        OperatorId = operatorId;
        ReporterUserId = reporterUserId;
        Category = category;
        Description = description;
        PhotoUrls = photoUrls;
        Latitude = latitude;
        Longitude = longitude;
        ReportedAt = reportedAt;
    }

    public override string EventType => "trip.incident.reported";

    public Guid IncidentId { get; }
    public Guid TripId { get; }
    public Guid OperatorId { get; }
    public Guid ReporterUserId { get; }
    public string Category { get; }
    public string? Description { get; }
    public IReadOnlyCollection<string>? PhotoUrls { get; }
    public decimal? Latitude { get; }
    public decimal? Longitude { get; }
    public DateTimeOffset ReportedAt { get; }
}
