using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

/// <summary>
/// Additive crew-facing booking fact emitted after a Booking reaches CONFIRMED.
/// </summary>
public sealed class BookingCreatedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "booking.booking.created";

    public BookingCreatedIntegrationEvent(
        Guid bookingId,
        string bookingCode,
        Guid tripId,
        IReadOnlyList<string> ticketCodes,
        IReadOnlyList<string> seatNumbers,
        DateTimeOffset departureDateTime,
        BookingLocationSnapshot pickup,
        BookingLocationSnapshot dropoff,
        Guid? driverUserId,
        Guid? assistantUserId,
        DateTimeOffset occurredAt)
    {
        if (bookingId == Guid.Empty) throw new ArgumentException("Booking id is required.", nameof(bookingId));
        if (tripId == Guid.Empty) throw new ArgumentException("Trip id is required.", nameof(tripId));
        if (string.IsNullOrWhiteSpace(bookingCode)) throw new ArgumentException("Booking code is required.", nameof(bookingCode));
        if (ticketCodes is null || ticketCodes.Count == 0) throw new ArgumentException("At least one ticket code is required.", nameof(ticketCodes));
        if (seatNumbers is null || seatNumbers.Count == 0) throw new ArgumentException("At least one seat number is required.", nameof(seatNumbers));
        if (seatNumbers.Count != ticketCodes.Count) throw new ArgumentException("Seat and ticket counts must match.", nameof(seatNumbers));
        if (departureDateTime == default) throw new ArgumentException("Departure time is required.", nameof(departureDateTime));

        EventId = Guid.NewGuid();
        OccurredAt = occurredAt.UtcDateTime;
        BookingId = bookingId;
        BookingCode = bookingCode.Trim();
        TripId = tripId;
        TicketCodes = ticketCodes;
        SeatNumbers = seatNumbers;
        DepartureDateTime = departureDateTime;
        PassengerCount = ticketCodes.Count;
        Pickup = pickup;
        Dropoff = dropoff;
        DriverUserId = driverUserId;
        AssistantUserId = assistantUserId;
    }

    [JsonIgnore]
    public override string EventType => EventTypeValue;

    public Guid BookingId { get; }
    public string BookingCode { get; }
    public Guid TripId { get; }
    public string Status => "CONFIRMED";
    public IReadOnlyList<string> TicketCodes { get; }
    public IReadOnlyList<string> SeatNumbers { get; }
    public DateTimeOffset DepartureDateTime { get; }
    public int PassengerCount { get; }
    public BookingLocationSnapshot Pickup { get; }
    public BookingLocationSnapshot Dropoff { get; }
    public Guid? DriverUserId { get; }
    public Guid? AssistantUserId { get; }
}

public sealed record BookingLocationSnapshot(
    Guid? StationId,
    Guid? StopId,
    string? Address);
