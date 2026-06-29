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

    public static PlatformWalletTransaction CreatePaymentHold(
        PlatformWalletTransactionType type,
        Money amount,
        Money balanceBefore,
        Money balanceAfter,
        PlatformWalletTransactionRef refType,
        Guid? referenceId = null,
        string? note = null)
    {
        if (referenceId == Guid.Empty)
            throw new ArgumentException("Reference id is required.", nameof(referenceId));

        return Create(
            type,
            amount,
            balanceBefore,
            balanceAfter,
            refType,
            referenceId,
            note);
    }

    public static PlatformWalletTransaction CreateBookingPaymentHold(
        Money amount,
        Money balanceBefore,
        Money balanceAfter,
        Guid bookingId,
        string? note = null)
    {
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking id is required.", nameof(bookingId));

        return Create(
            PlatformWalletTransactionType.CREDIT,
            amount,
            balanceBefore,
            balanceAfter,
            PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD,
            bookingId,
            note);
    }

    public static PlatformWalletTransaction CreateBookingRefund(
        Money amount,
        Money balanceBefore,
        Money balanceAfter,
        Guid bookingId,
        string? note = null)
    {
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking id is required.", nameof(bookingId));

        return Create(
            PlatformWalletTransactionType.DEBIT,
            amount,
            balanceBefore,
            balanceAfter,
            PlatformWalletTransactionRef.BOOKING_REFUND,
            bookingId,
            note);
    }
}
