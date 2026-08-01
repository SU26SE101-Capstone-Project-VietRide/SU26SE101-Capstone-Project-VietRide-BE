using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelRouteFare : BaseEntity<Guid>
{
    public Guid RouteId { get; private set; }
    public ParcelSizeCategory SizeCategory { get; private set; }
    public Guid OperatorId { get; private set; }
    public Money PriceVnd { get; private set; }
    public Money PricePerChargeableKgVnd { get; private set; }
    public Money MinimumPriceVnd { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveUntil { get; private set; }

    private ParcelRouteFare() { }

    public static ParcelRouteFare Create(
        Guid routeId,
        ParcelSizeCategory sizeCategory,
        Guid operatorId,
        Money priceVnd,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil = null)
    {
        return new ParcelRouteFare
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            SizeCategory = sizeCategory,
            OperatorId = operatorId,
            PriceVnd = priceVnd,
            PricePerChargeableKgVnd = priceVnd,
            MinimumPriceVnd = Money.Zero,
            EffectiveFrom = effectiveFrom,
            EffectiveUntil = effectiveUntil,
        };
    }

    public void UpdatePrice(Money newPrice)
    {
        PriceVnd = newPrice;
        PricePerChargeableKgVnd = newPrice;
    }

    public void AssignOperator(Guid operatorId)
    {
        OperatorId = operatorId;
    }

    public void UpdateWeightPricing(Money pricePerChargeableKg, Money minimumPrice)
    {
        PricePerChargeableKgVnd = pricePerChargeableKg;
        MinimumPriceVnd = minimumPrice;
    }

    public void UpdateEffectiveWindow(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveUntil)
    {
        if (effectiveUntil.HasValue && effectiveUntil <= effectiveFrom)
            throw new ArgumentException("EffectiveUntil must be after EffectiveFrom.");

        EffectiveFrom = effectiveFrom;
        EffectiveUntil = effectiveUntil;
    }
}
