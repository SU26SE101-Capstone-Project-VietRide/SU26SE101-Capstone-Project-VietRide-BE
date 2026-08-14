using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Domain.Entities;

/// <summary>
/// UPSERT-driven counter row for operator booking reporting.
/// </summary>
public sealed class BookingStats : BaseEntity<Guid>
{
    public Guid OperatorId { get; private set; }
    public string? OperatorName { get; private set; }
    public DateOnly StatDate { get; private set; }
    public Guid? TripId { get; private set; }
    public int TotalBookings { get; private set; }
    public int TotalConfirmed { get; private set; }
    public int TotalCancelled { get; private set; }
    public int TotalNoShow { get; private set; }
    public int TotalNoShowPassengers { get; private set; }
    public int TotalCompleted { get; private set; }
    public Money TotalRevenue { get; private set; }
    public Money TotalRefunded { get; private set; }
    public int TotalSeatsBooked { get; private set; }

    private BookingStats() { }

    public static BookingStats Create(
        Guid operatorId,
        DateOnly statDate,
        Guid? tripId,
        string? operatorName = null)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(operatorId));

        return new BookingStats
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            OperatorName = NormalizeOperatorName(operatorName),
            StatDate = statDate,
            TripId = tripId,
            TotalRevenue = Money.Zero,
            TotalRefunded = Money.Zero,
        };
    }

    public void SetOperatorName(string? operatorName)
    {
        OperatorName = NormalizeOperatorName(operatorName);
    }

    public void SetCounters(
        int totalBookings,
        int totalConfirmed,
        int totalCancelled,
        int totalNoShow,
        int totalCompleted,
        Money totalRevenue,
        Money totalRefunded,
        int totalSeatsBooked,
        int totalNoShowPassengers = 0)
    {
        EnsureNonNegative(totalBookings, nameof(totalBookings));
        EnsureNonNegative(totalConfirmed, nameof(totalConfirmed));
        EnsureNonNegative(totalCancelled, nameof(totalCancelled));
        EnsureNonNegative(totalNoShow, nameof(totalNoShow));
        EnsureNonNegative(totalCompleted, nameof(totalCompleted));
        EnsureNonNegative(totalRevenue.Amount, nameof(totalRevenue));
        EnsureNonNegative(totalRefunded.Amount, nameof(totalRefunded));
        EnsureNonNegative(totalSeatsBooked, nameof(totalSeatsBooked));
        EnsureNonNegative(totalNoShowPassengers, nameof(totalNoShowPassengers));

        TotalBookings = totalBookings;
        TotalConfirmed = totalConfirmed;
        TotalCancelled = totalCancelled;
        TotalNoShow = totalNoShow;
        TotalCompleted = totalCompleted;
        TotalRevenue = totalRevenue;
        TotalRefunded = totalRefunded;
        TotalSeatsBooked = totalSeatsBooked;
        TotalNoShowPassengers = totalNoShowPassengers;
    }

    private static string? NormalizeOperatorName(string? operatorName)
        => string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim();

    private static void EnsureNonNegative(long value, string paramName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(paramName, "Booking stats counters cannot be negative.");
    }
}
