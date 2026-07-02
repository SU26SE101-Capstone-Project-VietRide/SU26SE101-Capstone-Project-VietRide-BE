using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

/// <summary>
/// Immutable passenger wallet ledger row. No updates or soft-delete.
/// </summary>
public sealed class WalletTransaction : BaseEntity<Guid>
{
    private WalletTransaction() { }

    public Guid UserId { get; private set; }
    public WalletTransactionType Type { get; private set; }
    public Money Amount { get; private set; }
    public Money BalanceBefore { get; private set; }
    public Money BalanceAfter { get; private set; }
    public WalletTransactionRef ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? Note { get; private set; }

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

    public static WalletTransaction CreateBookingPaymentDebit(
        Guid userId,
        Guid bookingId,
        Money amount,
        Money balanceBefore,
        Money balanceAfter)
    {
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking id is required.", nameof(bookingId));

        return Create(
            userId,
            WalletTransactionType.DEBIT,
            amount,
            balanceBefore,
            balanceAfter,
            WalletTransactionRef.BOOKING_PAYMENT,
            bookingId,
            "Round-trip wallet booking payment");
    }

    public static WalletTransaction CreatePaymentDebit(
        Guid userId,
        Guid referenceId,
        Money amount,
        Money balanceBefore,
        Money balanceAfter,
        WalletTransactionRef refType,
        string? note = null)
    {
        if (referenceId == Guid.Empty)
            throw new ArgumentException("Reference id is required.", nameof(referenceId));

        return Create(
            userId,
            WalletTransactionType.DEBIT,
            amount,
            balanceBefore,
            balanceAfter,
            refType,
            referenceId,
            note);
    }

    public static WalletTransaction CreateRefundCredit(
        Guid userId,
        WalletTransactionRef referenceType,
        Guid referenceId,
        Money amount,
        Money balanceBefore,
        Money balanceAfter)
    {
        if (referenceType is not (WalletTransactionRef.BOOKING_REFUND or WalletTransactionRef.PARCEL_REFUND))
            throw new ArgumentException("Refund reference type is required.", nameof(referenceType));
        if (referenceId == Guid.Empty)
            throw new ArgumentException("Reference id is required.", nameof(referenceId));

        return Create(
            userId,
            WalletTransactionType.CREDIT,
            amount,
            balanceBefore,
            balanceAfter,
            referenceType,
            referenceId,
            referenceType == WalletTransactionRef.PARCEL_REFUND ? "Parcel refund" : "Booking refund");
    }

    public static WalletTransaction CreateBookingRefundCredit(
        Guid userId,
        Guid bookingId,
        Money amount,
        Money balanceBefore,
        Money balanceAfter)
        => CreateRefundCredit(
            userId,
            WalletTransactionRef.BOOKING_REFUND,
            bookingId,
            amount,
            balanceBefore,
            balanceAfter);
}
