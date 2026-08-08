using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class PassengerBoardedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.passenger.boarded";

    public PassengerBoardedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        string bookingCode,
        Guid tripId,
        Guid passengerRecordId,
        string seatNumber,
        string ticketCode)
        : base(eventId, occurredAt.UtcDateTime)
    {
        BookingId = bookingId;
        BookingCode = bookingCode;
        TripId = tripId;
        PassengerRecordId = passengerRecordId;
        SeatNumber = seatNumber;
        TicketCode = ticketCode;
        BoardedAt = occurredAt;
    }

    public Guid BookingId { get; }
    public string BookingCode { get; }
    public Guid TripId { get; }
    public Guid PassengerRecordId { get; }
    public string SeatNumber { get; }
    public string TicketCode { get; }
    public DateTimeOffset BoardedAt { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
