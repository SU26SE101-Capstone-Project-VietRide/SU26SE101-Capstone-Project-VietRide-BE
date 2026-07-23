using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingRouteChangeAutoFallbackAppliedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue =
        "booking.booking.route_change_auto_fallback_applied";

    public BookingRouteChangeAutoFallbackAppliedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        Guid tripId,
        Guid userId,
        Guid pendingActionId,
        Guid originalStopId,
        Guid fallbackDestinationStationId)
        : base(eventId, occurredAt.UtcDateTime)
    {
        BookingId = bookingId;
        TripId = tripId;
        UserId = userId;
        PendingActionId = pendingActionId;
        OriginalStopId = originalStopId;
        FallbackDestinationStationId = fallbackDestinationStationId;
    }

    public Guid BookingId { get; }
    public Guid TripId { get; }
    public Guid UserId { get; }
    public Guid PendingActionId { get; }
    public Guid OriginalStopId { get; }
    public Guid FallbackDestinationStationId { get; }
    public bool ShuttleRequired => true;
    public string ResolvedAction => "AUTO_FALLBACK_DESTINATION";
    public override string EventType => EventTypeValue;
}
