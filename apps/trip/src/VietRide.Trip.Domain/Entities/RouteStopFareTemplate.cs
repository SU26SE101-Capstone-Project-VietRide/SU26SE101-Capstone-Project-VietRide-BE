using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Fare template that starts from a specific stop on a route.
/// Window overlap is enforced later in the application layer.
/// </summary>
public sealed class RouteStopFareTemplate : BaseEntity<Guid>
{
    public Guid RouteId { get; private set; }
    public Guid StopId { get; private set; }
    public Money FareFromThisStop { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveUntil { get; private set; }

    private RouteStopFareTemplate() { }

    public static RouteStopFareTemplate Create(
        Guid routeId,
        Guid stopId,
        Money fareFromThisStop,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil = null)
    {
        ValidateGuid(routeId, nameof(routeId));
        ValidateGuid(stopId, nameof(stopId));
        ValidateEffectiveWindow(effectiveFrom, effectiveUntil);

        return new RouteStopFareTemplate
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            StopId = stopId,
            FareFromThisStop = fareFromThisStop,
            EffectiveFrom = effectiveFrom,
            EffectiveUntil = effectiveUntil,
        };
    }

    public void UpdateFare(Money fareFromThisStop)
    {
        FareFromThisStop = fareFromThisStop;
    }

    public void UpdateEffectiveWindow(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveUntil)
    {
        ValidateEffectiveWindow(effectiveFrom, effectiveUntil);

        EffectiveFrom = effectiveFrom;
        EffectiveUntil = effectiveUntil;
    }

    private static void ValidateEffectiveWindow(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveUntil)
    {
        if (effectiveUntil is not null && effectiveUntil <= effectiveFrom)
        {
            throw new ArgumentException("Effective until must be after effective from.", nameof(effectiveUntil));
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
