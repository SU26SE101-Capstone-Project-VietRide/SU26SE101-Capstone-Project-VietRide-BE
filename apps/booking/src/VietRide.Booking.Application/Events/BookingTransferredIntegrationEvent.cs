using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingTransferredIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.transferred";

    public BookingTransferredIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid sourceSubstitutionEventId,
        Guid bookingId,
        Guid recipientUserId,
        Guid operatorId,
        Guid oldTripId,
        Guid newTripId,
        Guid newVehicleId,
        string newVehiclePlateNumber,
        DateTimeOffset newTripDepartureDateTime,
        bool notifyPassengers,
        IReadOnlyCollection<Transfer> transfers)
        : base(eventId, occurredAt.UtcDateTime)
    {
        SourceSubstitutionEventId = sourceSubstitutionEventId;
        BookingId = bookingId;
        RecipientUserId = recipientUserId;
        OperatorId = operatorId;
        OldTripId = oldTripId;
        NewTripId = newTripId;
        NewVehicleId = newVehicleId;
        NewVehiclePlateNumber = newVehiclePlateNumber;
        NewTripDepartureDateTime = newTripDepartureDateTime;
        NotifyPassengers = notifyPassengers;
        Transfers = transfers;
    }

    public Guid SourceSubstitutionEventId { get; }
    public Guid BookingId { get; }
    public Guid RecipientUserId { get; }
    public Guid OperatorId { get; }
    public Guid OldTripId { get; }
    public Guid NewTripId { get; }
    public Guid NewVehicleId { get; }
    public string NewVehiclePlateNumber { get; }
    public DateTimeOffset NewTripDepartureDateTime { get; }
    public bool NotifyPassengers { get; }
    public IReadOnlyCollection<Transfer> Transfers { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;

    public sealed record Transfer(
        Guid PassengerId,
        string? OriginalSeatNumber,
        string? NewSeatNumber,
        string ConfirmationStatus);
}
