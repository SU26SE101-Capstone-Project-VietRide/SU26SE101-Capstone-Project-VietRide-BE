using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Domain.Entities;

/// <summary>
/// One application of a <see cref="Voucher"/> to a <see cref="Booking"/> at checkout.
/// Persisted as an audit/snapshot row: <see cref="FundedBy"/> snapshots
/// <see cref="Voucher.FundingType"/> at apply time so settlement reconcile is stable even if the
/// voucher's funding type changes later.
/// <para>
/// The row is physically DELETED when the booking is cancelled/refunded (v7:4562) —
/// <see cref="BookingId"/> FK is <c>ON DELETE CASCADE</c> to local <c>bookings</c>, but soft-delete
/// of a booking does NOT fire that cascade, so the Application compensation handler deletes the
/// usage row explicitly. <see cref="VoucherId"/> FK is <c>ON DELETE RESTRICT</c> (a voucher with
/// usages cannot be hard-deleted).
/// </para>
/// <para>
/// This table has no <c>updated_at</c> column — it is append/delete only. <see cref="UpdatedAt"/>
/// and <see cref="RowVersion"/> are inherited from the base but ignored in EF mapping.
/// </para>
/// </summary>
public sealed class VoucherUsage : BaseEntity<Guid>
{
    public Guid VoucherId { get; private set; }

    /// <summary>Logical FK identity.users — the buyer who redeemed the voucher.</summary>
    public Guid UserId { get; private set; }

    public Guid BookingId { get; private set; }

    /// <summary>Round-trip group — shared by the two legs of a round-trip booking for limit counting.</summary>
    public Guid? BookingGroupId { get; private set; }

    public Money DiscountAmount { get; private set; }

    /// <summary>Snapshot of <see cref="Voucher.FundingType"/> at apply time.</summary>
    public VoucherFundingType FundedBy { get; private set; }

    // Navigations (EF) — intra-service FKs only.
    public Voucher? Voucher { get; private set; }
    public Booking? Booking { get; private set; }

    private VoucherUsage() { }

    /// <summary>Creates a voucher-usage snapshot row at checkout.</summary>
    public static VoucherUsage Create(
        Guid voucherId,
        Guid userId,
        Guid bookingId,
        Guid? bookingGroupId,
        Money discountAmount,
        VoucherFundingType fundedBy)
    {
        if (discountAmount.Amount < 0)
            throw new ArgumentException("Discount amount cannot be negative.", nameof(discountAmount));

        return new VoucherUsage
        {
            Id = Guid.NewGuid(),
            VoucherId = voucherId,
            UserId = userId,
            BookingId = bookingId,
            BookingGroupId = bookingGroupId,
            DiscountAmount = discountAmount,
            FundedBy = fundedBy,
        };
    }
}
