using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Domain.Entities;

public sealed class OperatorLedgerEntry : BaseEntity<Guid>
{
    private OperatorLedgerEntry() { }

    public Guid OperatorId { get; private set; }
    public Guid? TripId { get; private set; }
    public OperatorLedgerEntryType EntryType { get; private set; }
    public OperatorLedgerAdjustmentReason? AdjustmentReason { get; private set; }
    public long Amount { get; private set; }
    public OperatorLedgerReferenceType ReferenceType { get; private set; }
    public Guid ReferenceId { get; private set; }
    public string? ReferenceCode { get; private set; }
    public Guid SourceEventId { get; private set; }
    public DateTimeOffset? OccurredAt { get; private set; }
    public long? OperatorFundedVoucherAmount { get; private set; }
    public string? Note { get; private set; }
    public FinancialActorType ActorType { get; private set; } = FinancialActorType.SYSTEM;
    public Guid? ActorUserId { get; private set; }
    public string? ActorDisplayName { get; private set; }
    public string? ActorEmail { get; private set; }
    public string? ActorRole { get; private set; }
    public bool ActorSnapshotResolved { get; private set; } = true;

    public static OperatorLedgerEntry Create(
        Guid operatorId,
        Guid? tripId,
        OperatorLedgerEntryType entryType,
        long amount,
        OperatorLedgerReferenceType referenceType,
        Guid referenceId,
        Guid sourceEventId,
        string? note = null,
        FinancialActorSnapshot? actor = null,
        OperatorLedgerAdjustmentReason? adjustmentReason = null,
        string? referenceCode = null,
        DateTimeOffset? occurredAt = null,
        long? operatorFundedVoucherAmount = null)
    {
        if (operatorId == Guid.Empty || referenceId == Guid.Empty || sourceEventId == Guid.Empty)
            throw new ArgumentException("Ledger identity fields are required.");
        if (tripId == Guid.Empty)
            throw new ArgumentException("Trip id cannot be empty.", nameof(tripId));
        if (referenceCode is not null
            && (string.IsNullOrWhiteSpace(referenceCode)
                || referenceCode.Length > 64
                || !string.Equals(referenceCode, referenceCode.Trim(), StringComparison.Ordinal)))
        {
            throw new ArgumentException("Reference code must be trimmed and at most 64 characters.", nameof(referenceCode));
        }

        ValidateAdjustment(entryType, amount, referenceType, adjustmentReason);

        var isRefund = entryType is OperatorLedgerEntryType.BOOKING_REFUND or
            OperatorLedgerEntryType.PARCEL_REFUND;
        var isAuditOnly = entryType == OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT;
        if (isRefund && amount >= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Refund ledger amount must be negative.");
        if (isAuditOnly && amount != 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Audit-only voucher amount must be zero.");
        if (operatorFundedVoucherAmount.HasValue
            && (!isAuditOnly || operatorFundedVoucherAmount.Value <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(operatorFundedVoucherAmount),
                "Operator-funded voucher amount is only valid as a positive audit amount.");
        }
        if (!isRefund && !isAuditOnly && entryType != OperatorLedgerEntryType.ADJUSTMENT && amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Revenue ledger amount must be positive.");

        return new OperatorLedgerEntry
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            TripId = tripId,
            EntryType = entryType,
            AdjustmentReason = adjustmentReason,
            Amount = amount,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceCode = referenceCode,
            SourceEventId = sourceEventId,
            OccurredAt = occurredAt,
            OperatorFundedVoucherAmount = operatorFundedVoucherAmount,
            Note = note,
            ActorType = actor is null ? FinancialActorType.SYSTEM : FinancialActorType.USER,
            ActorUserId = actor?.UserId,
            ActorDisplayName = actor?.DisplayName,
            ActorEmail = actor?.Email,
            ActorRole = actor?.Role,
        };
    }

    private static void ValidateAdjustment(
        OperatorLedgerEntryType entryType,
        long amount,
        OperatorLedgerReferenceType referenceType,
        OperatorLedgerAdjustmentReason? adjustmentReason)
    {
        if (entryType != OperatorLedgerEntryType.ADJUSTMENT)
        {
            if (adjustmentReason.HasValue)
                throw new ArgumentException("Only adjustment entries can have an adjustment reason.", nameof(adjustmentReason));
            return;
        }

        if (!adjustmentReason.HasValue)
            throw new ArgumentException("Adjustment entries require an adjustment reason.", nameof(adjustmentReason));

        var valid = adjustmentReason.Value switch
        {
            OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL =>
                amount < 0 && referenceType is OperatorLedgerReferenceType.BOOKING or OperatorLedgerReferenceType.PARCEL,
            OperatorLedgerAdjustmentReason.GENERIC_BOOKING_REFUND_ENTITLEMENT =>
                amount == 0 && referenceType == OperatorLedgerReferenceType.BOOKING,
            OperatorLedgerAdjustmentReason.MANUAL_WALLET_ADJUSTMENT =>
                amount != 0 && referenceType == OperatorLedgerReferenceType.MANUAL,
            OperatorLedgerAdjustmentReason.LEGACY_UNCLASSIFIED => false,
            _ => false,
        };

        if (!valid)
            throw new ArgumentException("Adjustment amount, reference, and reason are inconsistent.", nameof(adjustmentReason));
    }

    public void BackfillUserActor(FinancialActorSnapshot actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (ActorType != FinancialActorType.USER
            || (ActorUserId.HasValue && ActorUserId != actor.UserId))
        {
            throw new InvalidOperationException("Actor snapshot does not belong to this ledger entry.");
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
