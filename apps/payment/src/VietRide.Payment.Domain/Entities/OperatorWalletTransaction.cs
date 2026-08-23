using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Identifiers;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

public sealed class OperatorWalletTransaction : BaseEntity<Guid>, IBusinessCodeEntity
{
    string IBusinessCodeEntity.BusinessCodeConstraintName => "uq_operator_wallet_transactions_code";
    private OperatorWalletTransaction() { }

    public Guid OperatorId { get; private set; }
    public string? TransactionCode { get; private set; }
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
        string? note = null,
        DateTimeOffset? businessInstant = null)
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
            TransactionCode = BusinessCodeGenerator.Generate("OWT", businessInstant ?? DateTimeOffset.UtcNow),
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

    public void BackfillTransactionCode(DateTimeOffset businessInstant)
    {
        TransactionCode ??= BusinessCodeGenerator.Generate("OWT", businessInstant);
    }

    void IBusinessCodeEntity.RegenerateBusinessCode()
        => TransactionCode = BusinessCodeGenerator.Generate("OWT", CreatedAt);
}
