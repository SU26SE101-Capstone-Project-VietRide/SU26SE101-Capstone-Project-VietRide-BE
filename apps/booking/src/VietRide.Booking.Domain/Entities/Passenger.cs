using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Domain.Entities;

/// <summary>
/// Operational-only sub-entity of <see cref="Booking"/>. Represents one seat held in a booking.
/// No PII stored here (fullName/phoneNumber/idNumber are validated at the Application layer but
/// never persisted — see db-schema/booking/schema.sql COMMENT line 149).
/// Max 5 passengers per booking enforced at app layer (BOOKING_MAX_SEATS_EXCEEDED) and
/// at DB layer via trigger trg_passengers_max_5_per_booking.
/// </summary>
public sealed class Passenger : BaseEntity<Guid>
{
    public Guid BookingId { get; private set; }
    // EF permits NULL for unresolved replacement seats; newly created checkout passengers still require a seat.
    public string? SeatNumber { get; private set; }
    public PassengerBoardingStatus BoardingStatus { get; private set; } = PassengerBoardingStatus.PENDING;
    public DateTimeOffset? BoardedAt { get; private set; }

    /// <summary>Logical FK to trip.stops — set when passenger boards at a pickup stop.</summary>
    public Guid? BoardedAtStopId { get; private set; }

    // Navigation (EF)
    public Booking? Booking { get; private set; }
    public Ticket? Ticket { get; private set; }

    private Passenger() { }

    internal static Passenger Create(Guid bookingId, string seatNumber)
    {
        if (string.IsNullOrWhiteSpace(seatNumber))
            throw new ArgumentException("Seat number cannot be null or whitespace.", nameof(seatNumber));

        return new Passenger
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            SeatNumber = seatNumber.Trim(),
            BoardingStatus = PassengerBoardingStatus.PENDING,
        };
    }

    public void MarkBoarded(DateTimeOffset boardedAt, Guid? boardedAtStopId = null)
    {
        BoardingStatus = PassengerBoardingStatus.BOARDED;
        BoardedAt = boardedAt;
        BoardedAtStopId = boardedAtStopId;
    }

    public bool MarkNoShow()
    {
        if (BoardingStatus != PassengerBoardingStatus.PENDING)
        {
            return false;
        }

        BoardingStatus = PassengerBoardingStatus.NO_SHOW;
        return true;
    }

    public void ApplyVehicleSubstitutionSeat(string? seatNumber)
    {
        if (BoardingStatus is not PassengerBoardingStatus.BOARDED and not PassengerBoardingStatus.PENDING)
            throw new InvalidOperationException(
                $"Passenger boarding status {BoardingStatus} is not eligible for vehicle substitution.");
        if (seatNumber is not null
            && (string.IsNullOrWhiteSpace(seatNumber)
                || seatNumber.Length > 20
                || !string.Equals(seatNumber, seatNumber.Trim(), StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Mapped seat number must be null or an already-normalized value of at most 20 characters.",
                nameof(seatNumber));
        }

        SeatNumber = seatNumber;
    }
}
