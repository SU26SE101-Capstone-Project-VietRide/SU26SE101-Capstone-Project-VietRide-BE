using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class OperatorDepositPolicy : BaseEntity<Guid>
{
    public Guid OperatorId { get; private set; }
    public Guid? RouteId { get; private set; }
    public decimal DepositPercent { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; } = true;

    private OperatorDepositPolicy() { }

    public static OperatorDepositPolicy Create(
        Guid operatorId,
        Guid? routeId,
        decimal depositPercent,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo = null)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("Operator id is required.", nameof(operatorId));
        }

        if (depositPercent <= 0m || depositPercent > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(depositPercent), depositPercent, "Deposit percent must be between 0 and 100.");
        }

        return new OperatorDepositPolicy
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            RouteId = routeId,
            DepositPercent = depositPercent,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            IsActive = true,
        };
    }
}
