using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

/// <summary>
/// Immutable passenger wallet ledger row. No updates or soft-delete.
/// </summary>
public sealed class WalletTransaction : BaseEntity<Guid>
{
    public Guid UserId { get; private set; }
    public WalletTransactionType Type { get; private set; }
    public Money Amount { get; private set; }
    public Money BalanceBefore { get; private set; }
    public Money BalanceAfter { get; private set; }
    public WalletTransactionRef ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? Note { get; private set; }

    private WalletTransaction() { }

    public static WalletTransaction Create(
        Guid userId,
        WalletTransactionType type,
        Money amount,
        Money balanceBefore,
        Money balanceAfter,
        WalletTransactionRef referenceType,
        Guid? referenceId = null,
        string? note = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Transaction amount must be positive.");

        return new WalletTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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
