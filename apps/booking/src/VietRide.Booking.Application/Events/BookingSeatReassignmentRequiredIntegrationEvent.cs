using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingSeatReassignmentRequiredIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.seat_reassignment_required";

    public BookingSeatReassignmentRequiredIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        Guid tripId,
        Guid userId,
        Guid pendingActionId,
        DateTimeOffset deadline,
        IReadOnlyCollection<string> seatNumbers,
        string reason)
        : base(eventId, occurredAt.UtcDateTime)
    {
        BookingId = bookingId;
        TripId = tripId;
        UserId = userId;
        PendingActionId = pendingActionId;
        Deadline = deadline;
        SeatNumbers = seatNumbers;
        Reason = reason;
    }

    public Guid BookingId { get; }
    public Guid TripId { get; }
    public Guid UserId { get; }
    public Guid PendingActionId { get; }
    public DateTimeOffset Deadline { get; }
    public IReadOnlyCollection<string> SeatNumbers { get; }
    public string Reason { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
