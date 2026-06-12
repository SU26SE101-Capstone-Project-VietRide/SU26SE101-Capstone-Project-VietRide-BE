using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

public sealed class WalletTransaction
{
    private WalletTransaction()
    {
    }

    private WalletTransaction(
        Guid id,
        Guid userId,
        WalletTransactionType type,
        Money amount,
        Money balanceBefore,
        Money balanceAfter,
        WalletTransactionReferenceType referenceType,
        Guid? referenceId,
        string? note)
    {
        Id = id;
        UserId = userId;
        Type = type;
        Amount = amount;
        BalanceBefore = balanceBefore;
        BalanceAfter = balanceAfter;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        Note = note;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public WalletTransactionType Type { get; private set; }
    public Money Amount { get; private set; }
    public Money BalanceBefore { get; private set; }
    public Money BalanceAfter { get; private set; }
    public WalletTransactionReferenceType ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static WalletTransaction CreateBookingPaymentDebit(
        Guid userId,
        Guid bookingId,
        Money amount,
        Money balanceBefore,
        Money balanceAfter)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking id is required.", nameof(bookingId));
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Wallet transaction amount must be positive.");

        return new WalletTransaction(
            Guid.NewGuid(),
            userId,
            WalletTransactionType.DEBIT,
            amount,
            balanceBefore,
            balanceAfter,
            WalletTransactionReferenceType.BOOKING_PAYMENT,
            bookingId,
            "Round-trip wallet booking payment");
    }
}
