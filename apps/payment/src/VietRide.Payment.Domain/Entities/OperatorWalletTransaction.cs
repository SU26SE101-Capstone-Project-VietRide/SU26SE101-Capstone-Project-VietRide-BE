using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

public sealed class OperatorWalletTransaction : BaseEntity<Guid>
{
    private OperatorWalletTransaction() { }

    public Guid OperatorId { get; private set; }
    public OperatorWalletTransactionType Type { get; private set; }
    public Money Amount { get; private set; }
    public Money BalanceBefore { get; private set; }
    public Money BalanceAfter { get; private set; }
    public OperatorWalletTransactionRef ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? Note { get; private set; }

    public static OperatorWalletTransaction Create(
        Guid operatorId,
        OperatorWalletTransactionType type,
        Money amount,
        Money balanceBefore,
        Money balanceAfter,
        OperatorWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note = null)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(operatorId));
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (referenceType == OperatorWalletTransactionRef.SUBSCRIPTION_PAYMENT &&
            (!referenceId.HasValue || referenceId == Guid.Empty))
            throw new ArgumentException("Subscription payment reference id is required.", nameof(referenceId));

        return new OperatorWalletTransaction
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            Type = type,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Note = note,
        };
    }
}
