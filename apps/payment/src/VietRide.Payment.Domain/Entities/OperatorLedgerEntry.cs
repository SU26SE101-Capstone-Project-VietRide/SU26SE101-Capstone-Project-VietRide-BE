using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Domain.Entities;

public sealed class OperatorLedgerEntry : BaseEntity<Guid>
{
    private OperatorLedgerEntry() { }

    public Guid OperatorId { get; private set; }
    public Guid? TripId { get; private set; }
    public OperatorLedgerEntryType EntryType { get; private set; }
    public long Amount { get; private set; }
    public OperatorLedgerReferenceType ReferenceType { get; private set; }
    public Guid ReferenceId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public string? Note { get; private set; }

    public static OperatorLedgerEntry Create(
        Guid operatorId,
        Guid? tripId,
        OperatorLedgerEntryType entryType,
        long amount,
        OperatorLedgerReferenceType referenceType,
        Guid referenceId,
        Guid sourceEventId,
        string? note = null)
    {
        if (operatorId == Guid.Empty || referenceId == Guid.Empty || sourceEventId == Guid.Empty)
            throw new ArgumentException("Ledger identity fields are required.");
        if (tripId == Guid.Empty)
            throw new ArgumentException("Trip id cannot be empty.", nameof(tripId));

        var isRefund = entryType is OperatorLedgerEntryType.BOOKING_REFUND or
            OperatorLedgerEntryType.PARCEL_REFUND;
        var isAuditOnly = entryType == OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT;
        if (isRefund && amount >= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Refund ledger amount must be negative.");
        if (isAuditOnly && amount != 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Audit-only voucher amount must be zero.");
        if (!isRefund && !isAuditOnly && entryType != OperatorLedgerEntryType.ADJUSTMENT && amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Revenue ledger amount must be positive.");

        return new OperatorLedgerEntry
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            TripId = tripId,
            EntryType = entryType,
            Amount = amount,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            SourceEventId = sourceEventId,
            Note = note,
        };
    }
}
