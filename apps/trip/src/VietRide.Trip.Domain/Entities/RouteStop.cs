using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Stop sequence entry for a route. Composite key: RouteId + StopId.
/// </summary>
public sealed class RouteStop : IAuditable
{
    public Guid RouteId { get; private set; }
    public Guid StopId { get; private set; }
    public int OrderIndex { get; private set; }
    public int EstimatedDurationFromOriginMinutes { get; private set; }
    public decimal? DistanceFromOriginKm { get; private set; }
    public bool AllowPickup { get; private set; } = true;
    public bool AllowDropoff { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private RouteStop() { }

    public static RouteStop Create(
        Guid routeId,
        Guid stopId,
        int orderIndex,
        int estimatedDurationFromOriginMinutes,
        decimal? distanceFromOriginKm,
        bool allowPickup = true,
        bool allowDropoff = true)
    {
        ValidateGuid(routeId, nameof(routeId));
        ValidateGuid(stopId, nameof(stopId));
        ValidateOrder(orderIndex, nameof(orderIndex));
        ValidateDuration(estimatedDurationFromOriginMinutes, nameof(estimatedDurationFromOriginMinutes));
        ValidateOptionalDistance(distanceFromOriginKm, nameof(distanceFromOriginKm));
        ValidatePickupOrDropoff(allowPickup, allowDropoff);

        return new RouteStop
        {
            RouteId = routeId,
            StopId = stopId,
            OrderIndex = orderIndex,
            EstimatedDurationFromOriginMinutes = estimatedDurationFromOriginMinutes,
            DistanceFromOriginKm = distanceFromOriginKm,
            AllowPickup = allowPickup,
            AllowDropoff = allowDropoff,
        };
    }

    public void UpdateSequence(
        int orderIndex,
        int estimatedDurationFromOriginMinutes,
        decimal? distanceFromOriginKm)
    {
        ValidateOrder(orderIndex, nameof(orderIndex));
        ValidateDuration(estimatedDurationFromOriginMinutes, nameof(estimatedDurationFromOriginMinutes));
        ValidateOptionalDistance(distanceFromOriginKm, nameof(distanceFromOriginKm));

        OrderIndex = orderIndex;
        EstimatedDurationFromOriginMinutes = estimatedDurationFromOriginMinutes;
        DistanceFromOriginKm = distanceFromOriginKm;
    }

    public void UpdateBoardingPolicy(bool allowPickup, bool allowDropoff)
    {
        ValidatePickupOrDropoff(allowPickup, allowDropoff);

        AllowPickup = allowPickup;
        AllowDropoff = allowDropoff;
    }

    private static void ValidatePickupOrDropoff(bool allowPickup, bool allowDropoff)
    {
        if (!allowPickup && !allowDropoff)
        {
            throw new ArgumentException("Route stop must allow pickup or dropoff.", nameof(allowPickup));
        }
    }

    private static void ValidateOrder(int orderIndex, string parameterName)
    {
        if (orderIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, orderIndex, "Order index must be positive.");
        }
    }

    private static void ValidateDuration(int minutes, string parameterName)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, minutes, "Duration cannot be negative.");
        }
    }

    private static void ValidateOptionalDistance(decimal? distanceKm, string parameterName)
    {
        if (distanceKm < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, distanceKm, "Distance cannot be negative.");
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
