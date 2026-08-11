using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class TripAssignmentStartBlockedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.assignment.start_blocked";

    public TripAssignmentStartBlockedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid operatorId,
        string resourceRole,
        Guid resourceId,
        string conflictingSourceType,
        Guid conflictingSourceId,
        string conflictReason,
        DateTimeOffset? blockingUntil)
        : base(eventId, occurredAt.UtcDateTime)
    {
        TripId = tripId;
        OperatorId = operatorId;
        ResourceRole = resourceRole;
        ResourceId = resourceId;
        ConflictingSourceType = conflictingSourceType;
        ConflictingSourceId = conflictingSourceId;
        ConflictReason = conflictReason;
        BlockingUntil = blockingUntil;
    }

    public Guid TripId { get; }
    public Guid OperatorId { get; }
    public string ResourceRole { get; }
    public Guid ResourceId { get; }
    public string ConflictingSourceType { get; }
    public Guid ConflictingSourceId { get; }
    public string ConflictReason { get; }
    public DateTimeOffset? BlockingUntil { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
