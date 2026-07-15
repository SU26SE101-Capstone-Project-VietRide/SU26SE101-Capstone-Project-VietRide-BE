using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingScheduleChangeRequiredIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.schedule_change_required";

    public BookingScheduleChangeRequiredIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        Guid tripId,
        Guid userId,
        Guid pendingActionId,
        DateTimeOffset deadline,
        DateTimeOffset oldDeparture,
        DateTimeOffset newDeparture,
        string severity)
        : base(eventId, occurredAt.UtcDateTime)
    {
        if (severity is not ("MEDIUM" or "MAJOR"))
        {
            throw new ArgumentException("The action-required schedule contract accepts only MEDIUM or MAJOR.", nameof(severity));
        }

        BookingId = bookingId;
        TripId = tripId;
        UserId = userId;
        PendingActionId = pendingActionId;
        Deadline = deadline;
        OldDeparture = oldDeparture;
        NewDeparture = newDeparture;
        Severity = severity;
    }

    public Guid BookingId { get; }
    public Guid TripId { get; }
    public Guid UserId { get; }
    public Guid PendingActionId { get; }
    public DateTimeOffset Deadline { get; }
    public DateTimeOffset OldDeparture { get; }
    public DateTimeOffset NewDeparture { get; }
    public string Severity { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
