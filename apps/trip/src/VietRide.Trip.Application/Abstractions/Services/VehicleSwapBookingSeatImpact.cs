namespace VietRide.Trip.Application.Abstractions.Services;

public sealed record VehicleSwapBookingSeatImpact
{
    public const string SeatRemoved = "SEAT_REMOVED";
    public const string SeatDisabled = "SEAT_DISABLED";
    public const string SeatTypeDowngraded = "SEAT_TYPE_DOWNGRADED";

    public VehicleSwapBookingSeatImpact(
        Guid bookingId,
        IReadOnlyCollection<string> seatNumbers,
        string reason)
    {
        if (bookingId == Guid.Empty)
        {
            throw new ArgumentException("Booking id cannot be empty.", nameof(bookingId));
        }

        ArgumentNullException.ThrowIfNull(seatNumbers);
        if (!IsApprovedReason(reason))
        {
            throw new ArgumentException("Seat impact reason is not approved.", nameof(reason));
        }

        var normalizedSeatNumbers = seatNumbers
            .Select(NormalizeSeatNumber)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedSeatNumbers.Length == 0)
        {
            throw new ArgumentException("At least one impacted seat is required.", nameof(seatNumbers));
        }

        BookingId = bookingId;
        SeatNumbers = Array.AsReadOnly(normalizedSeatNumbers);
        Reason = reason;
    }

    public Guid BookingId { get; }

    public IReadOnlyList<string> SeatNumbers { get; }

    public string Reason { get; }

    public static bool IsApprovedReason(string? reason) => reason is
        SeatRemoved
        or SeatDisabled
        or SeatTypeDowngraded;

    private static string NormalizeSeatNumber(string seatNumber)
    {
        var normalized = seatNumber?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 20)
        {
            throw new ArgumentException("Seat number must contain 1 to 20 characters.", nameof(seatNumber));
        }

        return normalized;
    }
}
