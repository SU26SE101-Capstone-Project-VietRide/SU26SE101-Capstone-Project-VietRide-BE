using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public enum TripStopStatus
{
    PENDING,
    ARRIVED,
    SKIPPED,
}

/// <summary>
/// Immutable route-stop snapshot for a generated trip.
/// </summary>
public sealed class TripStop : BaseEntity<Guid>
{
    public Guid TripId { get; private set; }
    public Guid StopId { get; private set; }
    public int OrderIndex { get; private set; }
    public DateTimeOffset EstimatedArrivalTime { get; private set; }
    public DateTimeOffset? ActualArrivalTime { get; private set; }
    public TripStopStatus Status { get; private set; } = TripStopStatus.PENDING;
    public bool AllowPickup { get; private set; }
    public bool AllowDropoff { get; private set; }
    public decimal? DistanceFromOriginKm { get; private set; }

    private TripStop() { }

    public static TripStop Create(
        Guid tripId,
        Guid stopId,
        int orderIndex,
        DateTimeOffset estimatedArrivalTime,
        bool allowPickup,
        bool allowDropoff,
        decimal? distanceFromOriginKm)
    {
        ValidateGuid(tripId, nameof(tripId));
        ValidateGuid(stopId, nameof(stopId));
        ValidatePositive(orderIndex, nameof(orderIndex));
        ValidatePickupOrDropoff(allowPickup, allowDropoff);
        ValidateOptionalNonNegative(distanceFromOriginKm, nameof(distanceFromOriginKm));

        return new TripStop
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            StopId = stopId,
            OrderIndex = orderIndex,
            EstimatedArrivalTime = estimatedArrivalTime,
            Status = TripStopStatus.PENDING,
            AllowPickup = allowPickup,
            AllowDropoff = allowDropoff,
            DistanceFromOriginKm = distanceFromOriginKm,
        };
    }

    public void MarkArrived(DateTimeOffset actualArrivalTime)
    {
        EnsurePending(nameof(MarkArrived));
        ActualArrivalTime = actualArrivalTime;
        Status = TripStopStatus.ARRIVED;
    }

    public void MarkSkipped()
    {
        EnsurePending(nameof(MarkSkipped));
        Status = TripStopStatus.SKIPPED;
    }

    private void EnsurePending(string operation)
    {
        if (Status != TripStopStatus.PENDING)
        {
            throw new InvalidOperationException($"Trip stop must be pending before {operation}.");
        }
    }

    private static void ValidatePickupOrDropoff(bool allowPickup, bool allowDropoff)
    {
        if (!allowPickup && !allowDropoff)
        {
            throw new ArgumentException("At least one of pickup or dropoff must be allowed.", nameof(allowPickup));
        }
    }

    private static void ValidateOptionalNonNegative(decimal? value, string parameterName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }

    private static void ValidatePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
