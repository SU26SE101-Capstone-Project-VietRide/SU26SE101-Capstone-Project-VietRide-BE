using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

/// <summary>
/// Immutable platform wallet money ledger. Nullable display snapshots may be resolved once;
/// money, balance and reference fields are never updated or soft-deleted.
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
    public FinancialActorType ActorType { get; private set; } = FinancialActorType.SYSTEM;
    public Guid? ActorUserId { get; private set; }
    public string? ActorDisplayName { get; private set; }
    public string? ActorEmail { get; private set; }
    public string? ActorRole { get; private set; }
    public bool ActorSnapshotResolved { get; private set; } = true;

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

    public void AssignUserActor(FinancialActorSnapshot actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (ActorUserId.HasValue)
            throw new InvalidOperationException("Platform wallet transaction actor is immutable once assigned.");

        ActorType = FinancialActorType.USER;
        ActorUserId = actor.UserId;
        ActorDisplayName = actor.DisplayName;
        ActorEmail = actor.Email;
        ActorRole = actor.Role;
        ActorSnapshotResolved = true;
    }

    public void BackfillUserActor(FinancialActorSnapshot actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (ActorType != FinancialActorType.USER
            || (ActorUserId.HasValue && ActorUserId != actor.UserId))
        {
            throw new InvalidOperationException("Actor snapshot does not belong to this transaction.");
        }

        ActorUserId = actor.UserId;
        ActorDisplayName = actor.DisplayName;
        ActorEmail = actor.Email;
        ActorRole = actor.Role;
        ActorSnapshotResolved = true;
    }

    public void MarkUserActorSnapshotUnavailable()
    {
        if (ActorType != FinancialActorType.USER)
            throw new InvalidOperationException("Only user actor snapshots can be marked unavailable.");

        ActorDisplayName = null;
        ActorEmail = null;
        ActorRole = null;
        ActorSnapshotResolved = true;
    }
}
