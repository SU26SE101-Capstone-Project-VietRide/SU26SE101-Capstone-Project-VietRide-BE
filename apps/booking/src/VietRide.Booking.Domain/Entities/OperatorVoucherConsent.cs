using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Domain.Entities;

/// <summary>
/// An operator's opt-in decision on an admin-created <see cref="VoucherFundingType.OPERATOR_FUNDED"/>
/// voucher that targets it. One row per (operator, voucher) — enforced by
/// <c>uq_operator_voucher_consents_operator_voucher</c>.
/// <para>
/// Status machine: <see cref="OperatorVoucherConsentStatus.PENDING"/> (initial) →
/// <see cref="OperatorVoucherConsentStatus.ACCEPTED"/> or <see cref="OperatorVoucherConsentStatus.REJECTED"/>
/// via <see cref="Accept"/> / <see cref="Reject"/>. A revoke after accept (ACCEPTED → REJECTED) sets
/// <see cref="RespondedAt"/> but does NOT roll back discounts on already-CONFIRMED bookings.
/// </para>
/// <para>
/// Operator self-created vouchers do NOT produce consent rows (self-consented).
/// </para>
/// </summary>
public sealed class OperatorVoucherConsent : BaseEntity<Guid>
{
    /// <summary>Logical FK identity.operators — the operator being asked to opt in.</summary>
    public Guid OperatorId { get; private set; }

    public Guid VoucherId { get; private set; }

    public OperatorVoucherConsentStatus Status { get; private set; }
        = OperatorVoucherConsentStatus.PENDING;

    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }

    /// <summary>Logical FK identity.users — the OPERATOR_ADMIN who responded.</summary>
    public Guid? RespondedByUserId { get; private set; }

    public string? RejectReason { get; private set; }

    // Navigation (EF) — intra-service FK to vouchers (ON DELETE CASCADE).
    public Voucher? Voucher { get; private set; }

    private OperatorVoucherConsent() { }

    /// <summary>Creates a new PENDING consent row for an operator targeted by an admin voucher.</summary>
    public static OperatorVoucherConsent Create(Guid operatorId, Guid voucherId, DateTimeOffset requestedAt)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id must not be empty.", nameof(operatorId));
        if (voucherId == Guid.Empty)
            throw new ArgumentException("Voucher id must not be empty.", nameof(voucherId));

        return new OperatorVoucherConsent
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            VoucherId = voucherId,
            Status = OperatorVoucherConsentStatus.PENDING,
            RequestedAt = requestedAt,
        };
    }

    /// <summary>
    /// Accepts the consent (PENDING → ACCEPTED). Records who responded and when.
    /// </summary>
    public void Accept(Guid respondedByUserId, DateTimeOffset respondedAt)
    {
        if (Status != OperatorVoucherConsentStatus.PENDING)
            throw new InvalidOperationException($"Cannot accept a consent in status {Status}.");

        Status = OperatorVoucherConsentStatus.ACCEPTED;
        RespondedByUserId = respondedByUserId;
        RespondedAt = respondedAt;
        RejectReason = null;
    }

    /// <summary>
    /// Rejects (or revokes) the consent (PENDING|ACCEPTED → REJECTED). Records who responded and when.
    /// An optional reason may be supplied. Revoking an ACCEPTED consent does not roll back
    /// discounts on already-CONFIRMED bookings.
    /// </summary>
    public void Reject(Guid respondedByUserId, DateTimeOffset respondedAt, string? reason = null)
    {
        if (Status is not (OperatorVoucherConsentStatus.PENDING
            or OperatorVoucherConsentStatus.ACCEPTED))
        {
            throw new InvalidOperationException($"Cannot reject a consent in status {Status}.");
        }

        Status = OperatorVoucherConsentStatus.REJECTED;
        RespondedByUserId = respondedByUserId;
        RespondedAt = respondedAt;
        RejectReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
