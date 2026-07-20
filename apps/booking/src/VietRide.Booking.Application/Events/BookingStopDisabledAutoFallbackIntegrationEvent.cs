using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingStopDisabledAutoFallbackIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.stop_disabled_auto_fallback_applied";

    public BookingStopDisabledAutoFallbackIntegrationEvent(Guid eventId, DateTimeOffset occurredAt,
        Guid bookingId, Guid tripId, Guid userId, Guid pendingActionId, Guid disabledStopId,
        string affectedField, Guid fallbackStationId)
        : base(eventId, occurredAt.UtcDateTime)
    {
        BookingId = bookingId; TripId = tripId; UserId = userId; PendingActionId = pendingActionId;
        DisabledStopId = disabledStopId; AffectedField = affectedField;
        FallbackStationId = fallbackStationId; ResolvedAction = "AUTO_FALLBACK_DESTINATION";
    }

    public Guid BookingId { get; }
    public Guid TripId { get; }
    public Guid UserId { get; }
    public Guid PendingActionId { get; }
    public Guid DisabledStopId { get; }
    public string AffectedField { get; }
    public Guid FallbackStationId { get; }
    public string ResolvedAction { get; }

    public override string EventType => EventTypeValue;
}
