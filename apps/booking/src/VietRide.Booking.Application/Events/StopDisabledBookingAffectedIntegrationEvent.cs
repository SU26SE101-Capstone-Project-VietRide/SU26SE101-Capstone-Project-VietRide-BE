using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class StopDisabledBookingAffectedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.stop_disabled.affected";

    public StopDisabledBookingAffectedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid stopId,
        Guid? replacedByStopId,
        IReadOnlyCollection<Guid> recipientUserIds,
        int affectedBookingCount)
        : base(eventId, occurredAt.UtcDateTime)
    {
        StopId = stopId;
        ReplacedByStopId = replacedByStopId;
        RecipientUserIds = recipientUserIds.Distinct().ToArray();
        AffectedBookingCount = affectedBookingCount;
    }

    public Guid StopId { get; }
    public Guid? ReplacedByStopId { get; }
    public IReadOnlyCollection<Guid> RecipientUserIds { get; }
    public int AffectedBookingCount { get; }

    public override string EventType => EventTypeValue;
}
