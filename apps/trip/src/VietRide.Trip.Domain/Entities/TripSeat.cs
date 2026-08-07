using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

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

        var normalizedSeatNumber = ValidateSeatNumber(seatNumber);
        if (status == TripSeatStatus.UNAVAILABLE && string.IsNullOrWhiteSpace(disabledReason))
        {
            throw new ArgumentException("An unavailable seat requires a reason.", nameof(disabledReason));
        }
        if (status != TripSeatStatus.UNAVAILABLE && disabledReason is not null)
        {
            throw new ArgumentException("Only unavailable seats may have a disabled reason.", nameof(disabledReason));
        }

        return new TripSeat
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            SeatNumber = normalizedSeatNumber.ToUpperInvariant(),
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

    public void Disable(string reason)
    {
        EnsureStatus(TripSeatStatus.AVAILABLE, nameof(Disable));
        DisabledReason = ValidateRequired(reason, nameof(reason));
        Status = TripSeatStatus.UNAVAILABLE;
    }

    public void Enable()
    {
        EnsureStatus(TripSeatStatus.UNAVAILABLE, nameof(Enable));
        Status = TripSeatStatus.AVAILABLE;
        DisabledReason = null;
    }

    public bool ReconfigureAvailable(TripSeatType seatType)
    {
        if (seatType == TripSeatType.DRIVER_AREA)
        {
            throw new ArgumentException("Driver-area entries are not passenger seats.", nameof(seatType));
        }

        EnsureStatus(TripSeatStatus.AVAILABLE, nameof(ReconfigureAvailable));
        if (SeatType == seatType && DisabledReason is null)
        {
            return false;
        }

        SeatType = seatType;
        DisabledReason = null;
        return true;
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
