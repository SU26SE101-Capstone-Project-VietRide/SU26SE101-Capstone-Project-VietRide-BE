using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Domain.Entities;

public sealed class ProcessedIntegrationEvent : BaseEntity<Guid>
{
    private ProcessedIntegrationEvent() { }

    public string Consumer { get; private set; } = string.Empty;
    public Guid EventId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    public static ProcessedIntegrationEvent Create(
        string consumer,
        Guid eventId,
        DateTimeOffset processedAt)
    {
        if (string.IsNullOrWhiteSpace(consumer))
            throw new ArgumentException("Consumer is required.", nameof(consumer));
        if (eventId == Guid.Empty)
            throw new ArgumentException("Event id is required.", nameof(eventId));

        return new ProcessedIntegrationEvent
        {
            Id = Guid.NewGuid(),
            Consumer = consumer,
            EventId = eventId,
            ProcessedAt = processedAt,
        };
    }
}
