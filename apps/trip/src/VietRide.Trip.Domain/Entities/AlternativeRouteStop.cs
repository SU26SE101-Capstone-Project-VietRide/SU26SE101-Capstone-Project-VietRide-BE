using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Stop sequence entry for an alternative route. Composite key: AlternativeRouteId + StopId.
/// </summary>
public sealed class AlternativeRouteStop : IAuditable
{
    public Guid AlternativeRouteId { get; private set; }
    public Guid StopId { get; private set; }
    public int OrderIndex { get; private set; }
    public int EstimatedDurationFromOriginMinutes { get; private set; }
    public decimal? DistanceFromOriginKm { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private AlternativeRouteStop() { }

    public static AlternativeRouteStop Create(
        Guid alternativeRouteId,
        Guid stopId,
        int orderIndex,
        int estimatedDurationFromOriginMinutes,
        decimal? distanceFromOriginKm)
    {
        ValidateGuid(alternativeRouteId, nameof(alternativeRouteId));
        ValidateGuid(stopId, nameof(stopId));
        ValidateOrder(orderIndex, nameof(orderIndex));
        ValidateDuration(estimatedDurationFromOriginMinutes, nameof(estimatedDurationFromOriginMinutes));
        ValidateOptionalDistance(distanceFromOriginKm, nameof(distanceFromOriginKm));

        return new AlternativeRouteStop
        {
            AlternativeRouteId = alternativeRouteId,
            StopId = stopId,
            OrderIndex = orderIndex,
            EstimatedDurationFromOriginMinutes = estimatedDurationFromOriginMinutes,
            DistanceFromOriginKm = distanceFromOriginKm,
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
