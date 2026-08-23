using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class TripCompletedIntegrationEvent(
    Guid tripId,
    Guid operatorId,
    DateTimeOffset terminalAt,
    bool hasSubstitution,
    string? tripCode = null) : IntegrationEventBase
{
    public override string EventType => "trip.trip.completed";

    public Guid TripId { get; } = tripId;
    public Guid OperatorId { get; } = operatorId;
    public DateTimeOffset TerminalAt { get; } = terminalAt;
    public DateTimeOffset CompletedAt => TerminalAt;
    public bool HasSubstitution { get; } = hasSubstitution;
    public string? TripCode { get; } = tripCode;
}

public sealed class TripDisruptedIntegrationEvent(
    Guid tripId,
    Guid operatorId,
    DateTimeOffset terminalAt,
    bool hasSubstitution,
    string reason,
    string? tripCode = null) : IntegrationEventBase
{
    [JsonIgnore]
    public override string EventType => "trip.trip.disrupted";

    public Guid TripId { get; } = tripId;
    public Guid OperatorId { get; } = operatorId;
    public DateTimeOffset TerminalAt { get; } = terminalAt;
    public bool HasSubstitution { get; } = hasSubstitution;
    public string Reason { get; } = reason;
    public string? TripCode { get; } = tripCode;

    public TripDisruptedIntegrationEvent(
        Guid eventId,
        Guid tripId,
        Guid operatorId,
        DateTimeOffset terminalAt,
        bool hasSubstitution,
        string reason,
        string? tripCode = null)
        : this(tripId, operatorId, terminalAt, hasSubstitution, reason, tripCode)
    {
        EventId = eventId;
        OccurredAt = terminalAt.UtcDateTime;
    }
}
