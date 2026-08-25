using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingSeatShortageDetectedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.seat_shortage_detected";

    public BookingSeatShortageDetectedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid sourceSubstitutionEventId,
        Guid bookingId,
        string bookingCode,
        Guid operatorId,
        Guid oldTripId,
        Guid newTripId,
        int affectedPassengerCount,
        IReadOnlyCollection<string> originalSeatNumbers)
        : base(eventId, occurredAt.UtcDateTime)
    {
        SourceSubstitutionEventId = sourceSubstitutionEventId;
        BookingId = bookingId;
        BookingCode = bookingCode;
        OperatorId = operatorId;
        OldTripId = oldTripId;
        NewTripId = newTripId;
        AffectedPassengerCount = affectedPassengerCount;
        OriginalSeatNumbers = originalSeatNumbers;
    }

    public Guid SourceSubstitutionEventId { get; }
    public Guid BookingId { get; }
    public string BookingCode { get; }
    public Guid OperatorId { get; }
    public Guid OldTripId { get; }
    public Guid NewTripId { get; }
    public int AffectedPassengerCount { get; }
    public IReadOnlyCollection<string> OriginalSeatNumbers { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
