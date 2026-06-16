using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public enum TripSeatStatus
{
    AVAILABLE,
    HELD,
    BOOKED,
    UNAVAILABLE,
}

public enum TripSeatType
{
    STANDARD,
    SLEEPER_LOWER,
    SLEEPER_UPPER,
    VIP,
    DRIVER_AREA,
}

/// <summary>
/// Seat state snapshot for a generated trip.
/// </summary>
public sealed class TripSeat : BaseEntity<Guid>
{
    public Guid TripId { get; private set; }
    public string SeatNumber { get; private set; } = string.Empty;
    public TripSeatType SeatType { get; private set; } = TripSeatType.STANDARD;
    public TripSeatStatus Status { get; private set; } = TripSeatStatus.AVAILABLE;
    public string? DisabledReason { get; private set; }

    private TripSeat() { }

    public static TripSeat Create(Guid tripId, string seatNumber, TripSeatType seatType = TripSeatType.STANDARD)
    {
        ValidateGuid(tripId, nameof(tripId));
        var normalizedSeatNumber = ValidateSeatNumber(seatNumber);

        return new TripSeat
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            SeatNumber = normalizedSeatNumber,
            SeatType = seatType,
            Status = TripSeatStatus.AVAILABLE,
        };
    }

    public void MarkHeld()
    {
        EnsureStatus(TripSeatStatus.AVAILABLE, nameof(MarkHeld));
        Status = TripSeatStatus.HELD;
    }

    public void MarkBooked()
    {
        EnsureStatus(TripSeatStatus.HELD, nameof(MarkBooked));
        Status = TripSeatStatus.BOOKED;
    }

    public void Release()
    {
        EnsureStatus(TripSeatStatus.HELD, nameof(Release));
        Status = TripSeatStatus.AVAILABLE;
        DisabledReason = null;
    }

    public void MarkUnavailable(string reason)
    {
        DisabledReason = ValidateRequired(reason, nameof(reason));
        Status = TripSeatStatus.UNAVAILABLE;
    }

    private void EnsureStatus(TripSeatStatus expected, string operation)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Seat must be {expected} before {operation}.");
        }
    }

    private static string ValidateSeatNumber(string seatNumber)
    {
        var normalizedSeatNumber = seatNumber?.Trim() ?? string.Empty;
        if (normalizedSeatNumber.Length == 0)
        {
            throw new ArgumentException("Seat number is required.", nameof(seatNumber));
        }

        if (normalizedSeatNumber.Length > 20)
        {
            throw new ArgumentException("Seat number cannot exceed 20 characters.", nameof(seatNumber));
        }

        return normalizedSeatNumber;
    }

    private static string ValidateRequired(string value, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return normalized;
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
