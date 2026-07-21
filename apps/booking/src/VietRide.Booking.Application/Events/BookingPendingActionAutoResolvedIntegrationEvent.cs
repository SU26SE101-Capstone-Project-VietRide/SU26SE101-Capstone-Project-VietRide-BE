using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingPendingActionAutoResolvedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.pending_action_auto_resolved";

    public BookingPendingActionAutoResolvedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        Guid tripId,
        Guid userId,
        Guid pendingActionId,
        string resolvedAction,
        string severity,
        DateTimeOffset oldDeparture,
        DateTimeOffset newDeparture)
        : base(eventId, occurredAt.UtcDateTime)
    {
        if (resolvedAction != "ACCEPTED")
        {
            throw new ArgumentException("Timeout resolution must accept the pending action.", nameof(resolvedAction));
        }

        if (severity is not ("MEDIUM" or "MAJOR"))
        {
            throw new ArgumentException("Timeout resolution accepts only MEDIUM or MAJOR.", nameof(severity));
        }

        BookingId = bookingId;
        TripId = tripId;
        UserId = userId;
        PendingActionId = pendingActionId;
        ResolvedAction = resolvedAction;
        Severity = severity;
        OldDeparture = oldDeparture;
        NewDeparture = newDeparture;
    }

    public Guid BookingId { get; }
    public Guid TripId { get; }
    public Guid UserId { get; }
    public Guid PendingActionId { get; }
    public string ResolvedAction { get; }
    public string Severity { get; }
    public DateTimeOffset OldDeparture { get; }
    public DateTimeOffset NewDeparture { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
