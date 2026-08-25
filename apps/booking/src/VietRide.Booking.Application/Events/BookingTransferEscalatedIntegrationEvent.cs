using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingTransferEscalatedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.transfer_escalated";

    public BookingTransferEscalatedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        string bookingCode,
        Guid operatorId,
        Guid oldTripId,
        Guid newTripId,
        IReadOnlyCollection<Guid> transferIds,
        DateTimeOffset oldestTransferredAt)
        : base(eventId, occurredAt.UtcDateTime)
    {
        BookingId = bookingId;
        BookingCode = bookingCode;
        OperatorId = operatorId;
        OldTripId = oldTripId;
        NewTripId = newTripId;
        TransferIds = transferIds;
        PendingConfirmationCount = transferIds.Count;
        OldestTransferredAt = oldestTransferredAt;
    }

    public Guid BookingId { get; }
    public string BookingCode { get; }
    public Guid OperatorId { get; }
    public Guid OldTripId { get; }
    public Guid NewTripId { get; }
    public IReadOnlyCollection<Guid> TransferIds { get; }
    public int PendingConfirmationCount { get; }
    public DateTimeOffset OldestTransferredAt { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
