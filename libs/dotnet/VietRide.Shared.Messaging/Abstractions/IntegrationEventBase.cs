namespace VietRide.Shared.Messaging.Abstractions;

/// <summary>
/// Default base class for integration events. Auto-assigns
/// <see cref="EventId"/> + <see cref="OccurredAt"/>; concrete events supply
/// <see cref="EventType"/>.
/// </summary>
public abstract class IntegrationEventBase : IIntegrationEvent
{
    /// <summary>Default ctor — generates a new EventId + sets OccurredAt to now.</summary>
    protected IntegrationEventBase()
    {
        EventId = Guid.NewGuid();
        OccurredAt = DateTime.UtcNow;
    }

    /// <summary>Rehydration ctor — used when reconstituting from outbox row.</summary>
    protected IntegrationEventBase(Guid eventId, DateTime occurredAt)
    {
        EventId = eventId;
        OccurredAt = occurredAt;
    }

    /// <inheritdoc />
    public Guid EventId { get; init; }

    /// <inheritdoc />
    public DateTime OccurredAt { get; init; }

    /// <inheritdoc />
    public abstract string EventType { get; }
}
