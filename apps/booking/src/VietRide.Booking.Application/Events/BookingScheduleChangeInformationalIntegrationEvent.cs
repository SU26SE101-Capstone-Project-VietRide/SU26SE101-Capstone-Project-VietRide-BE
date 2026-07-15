using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingScheduleChangeInformationalIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.schedule_change_informational";

    public BookingScheduleChangeInformationalIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        Guid tripId,
        Guid userId,
        DateTimeOffset oldDeparture,
        DateTimeOffset newDeparture,
        string severity)
        : base(eventId, occurredAt.UtcDateTime)
    {
        if (severity != "MINOR")
        {
            throw new ArgumentException("The informational schedule contract accepts only MINOR.", nameof(severity));
        }

        BookingId = bookingId;
        TripId = tripId;
        UserId = userId;
        OldDeparture = oldDeparture;
        NewDeparture = newDeparture;
        Severity = severity;
    }

    public Guid BookingId { get; }
    public Guid TripId { get; }
    public Guid UserId { get; }
    public DateTimeOffset OldDeparture { get; }
    public DateTimeOffset NewDeparture { get; }
    public string Severity { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
