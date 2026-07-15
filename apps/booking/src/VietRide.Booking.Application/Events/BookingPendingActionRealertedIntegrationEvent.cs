using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingPendingActionRealertedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.pending_action_realerted";

    public BookingPendingActionRealertedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        Guid tripId,
        Guid userId,
        Guid pendingActionId,
        DateTimeOffset deadline,
        string reason,
        IReadOnlyCollection<string>? seatNumbers = null,
        string? seatImpactReason = null,
        DateTimeOffset? oldDeparture = null,
        DateTimeOffset? newDeparture = null,
        string? severity = null)
        : base(eventId, occurredAt.UtcDateTime)
    {
        BookingId = bookingId;
        TripId = tripId;
        UserId = userId;
        PendingActionId = pendingActionId;
        Deadline = deadline;
        Reason = reason;
        SeatNumbers = seatNumbers;
        SeatImpactReason = seatImpactReason;
        OldDeparture = oldDeparture;
        NewDeparture = newDeparture;
        Severity = severity;
    }

    public Guid BookingId { get; }
    public Guid TripId { get; }
    public Guid UserId { get; }
    public Guid PendingActionId { get; }
    public DateTimeOffset Deadline { get; }
    public string Reason { get; }
    public IReadOnlyCollection<string>? SeatNumbers { get; }
    public string? SeatImpactReason { get; }
    public DateTimeOffset? OldDeparture { get; }
    public DateTimeOffset? NewDeparture { get; }
    public string? Severity { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
