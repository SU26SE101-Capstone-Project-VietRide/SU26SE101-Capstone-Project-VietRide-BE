using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class TripCompletedIntegrationEvent(
    Guid tripId,
    Guid operatorId,
    DateTimeOffset terminalAt,
    bool hasSubstitution) : IntegrationEventBase
{
    public override string EventType => "trip.trip.completed";

    public Guid TripId { get; } = tripId;
    public Guid OperatorId { get; } = operatorId;
    public DateTimeOffset TerminalAt { get; } = terminalAt;
    public bool HasSubstitution { get; } = hasSubstitution;
}

public sealed class TripDisruptedIntegrationEvent(
    Guid tripId,
    Guid operatorId,
    DateTimeOffset terminalAt,
    bool hasSubstitution) : IntegrationEventBase
{
    public override string EventType => "trip.trip.disrupted";

    public Guid TripId { get; } = tripId;
    public Guid OperatorId { get; } = operatorId;
    public DateTimeOffset TerminalAt { get; } = terminalAt;
    public bool HasSubstitution { get; } = hasSubstitution;
}
