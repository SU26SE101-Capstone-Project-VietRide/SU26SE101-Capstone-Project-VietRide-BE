using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

/// <summary>
/// Immutable platform wallet ledger row. No updates or soft-delete.
/// </summary>
public sealed class PlatformWalletTransaction : BaseEntity<Guid>
{
    public PlatformWalletTransactionType Type { get; private set; }
    public Money Amount { get; private set; }
    public Money BalanceBefore { get; private set; }
    public Money BalanceAfter { get; private set; }
    public PlatformWalletTransactionRef ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? Note { get; private set; }

    private PlatformWalletTransaction() { }

    public static PlatformWalletTransaction Create(
        PlatformWalletTransactionType type,
        Money amount,
        Money balanceBefore,
        Money balanceAfter,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId = null,
        string? note = null)
    {
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Transaction amount must be positive.");

        return new PlatformWalletTransaction
        {
            Id = Guid.NewGuid(),
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
