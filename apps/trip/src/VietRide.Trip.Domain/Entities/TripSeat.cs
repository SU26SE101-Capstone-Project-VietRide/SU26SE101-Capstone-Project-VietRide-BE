using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class TripSeat : BaseEntity<Guid>
{
    public Guid TripId { get; private set; }
    public string SeatNumber { get; private set; } = string.Empty;
    public TripSeatType SeatType { get; private set; } = TripSeatType.STANDARD;
    public TripSeatStatus Status { get; private set; } = TripSeatStatus.AVAILABLE;
    public string? DisabledReason { get; private set; }

    private TripSeat() { }

    public static TripSeat Create(
        Guid tripId,
        string seatNumber,
        TripSeatType seatType = TripSeatType.STANDARD,
        TripSeatStatus status = TripSeatStatus.AVAILABLE,
        string? disabledReason = null)
    {
        if (tripId == Guid.Empty)
        {
            throw new ArgumentException("Trip id cannot be empty.", nameof(tripId));
        }

        if (string.IsNullOrWhiteSpace(seatNumber))
        {
            throw new ArgumentException("Seat number is required.", nameof(seatNumber));
        }

        return new TripSeat
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            SeatNumber = seatNumber.Trim().ToUpperInvariant(),
            SeatType = seatType,
            Status = status,
            DisabledReason = disabledReason,
        };
    }

    public void Hold()
    {
        if (Status != TripSeatStatus.AVAILABLE)
        {
            throw new InvalidOperationException("Only available seats can be held.");
        }

        Status = TripSeatStatus.HELD;
    }
}
