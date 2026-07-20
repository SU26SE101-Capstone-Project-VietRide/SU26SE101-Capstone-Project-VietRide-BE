using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingPassengerNoShowMarkedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.passenger_no_show_marked";

    public BookingPassengerNoShowMarkedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        Guid tripId,
        Guid userId,
        string bookingStatus,
        IReadOnlyCollection<Guid> newlyNoShowPassengerIds,
        string triggerType,
        Guid? pickupStopId)
        : base(eventId, occurredAt.UtcDateTime)
    {
        BookingId = bookingId;
        TripId = tripId;
        UserId = userId;
        BookingStatus = bookingStatus;
        NewlyNoShowPassengerIds = newlyNoShowPassengerIds.ToArray();
        TriggerType = triggerType;
        PickupStopId = pickupStopId;
    }

    public Guid BookingId { get; }
    public Guid TripId { get; }
    public Guid UserId { get; }
    public string BookingStatus { get; }
    public IReadOnlyCollection<Guid> NewlyNoShowPassengerIds { get; }
    public string TriggerType { get; }
    public Guid? PickupStopId { get; }
    public override string EventType => EventTypeValue;
}
