using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Services;

public sealed class RevenueLedgerWriter : IRevenueLedgerWriter
{
    private readonly IOperatorLedgerEntryRepository _ledger;

    public RevenueLedgerWriter(IOperatorLedgerEntryRepository ledger)
    {
        _ledger = ledger;
    }

    public async Task RecordPaymentSucceededAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in context.Allocations)
        {
            var paidAmount = checked(
                allocation.GrossAmount
                - allocation.VoucherVietRideFundedAmount
                - allocation.VoucherOperatorFundedAmount);
            var entryType = allocation.ReferenceType == "BOOKING"
                ? OperatorLedgerEntryType.BOOKING_REVENUE
                : OperatorLedgerEntryType.PARCEL_REVENUE;
            var referenceType = allocation.ReferenceType == "BOOKING"
                ? OperatorLedgerReferenceType.BOOKING
                : OperatorLedgerReferenceType.PARCEL;

            if (paidAmount > 0)
            {
                await _ledger.AddAsync(
                    OperatorLedgerEntry.Create(
                        allocation.OperatorId,
                        allocation.TripId,
                        entryType,
                        paidAmount,
                        referenceType,
                        allocation.ReferenceId,
                        sourceEventId),
                    cancellationToken);
            }

            if (allocation.VoucherVietRideFundedAmount > 0)
            {
                await _ledger.AddAsync(
                    OperatorLedgerEntry.Create(
                        allocation.OperatorId,
                        allocation.TripId,
                        OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT,
                        allocation.VoucherVietRideFundedAmount,
                        referenceType,
                        allocation.ReferenceId,
                        sourceEventId),
                    cancellationToken);
            }

            if (allocation.VoucherOperatorFundedAmount > 0)
            {
                await _ledger.AddAsync(
                    OperatorLedgerEntry.Create(
                        allocation.OperatorId,
                        allocation.TripId,
                        OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT,
                        0,
                        referenceType,
                        allocation.ReferenceId,
                        sourceEventId,
                        $"operator-funded-voucher:{allocation.VoucherOperatorFundedAmount}"),
                    cancellationToken);
            }
        }
    }

    public async Task RecordRefundAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        CancellationToken cancellationToken)
    {
        if (refundAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(refundAmount));

        var allocation = context.Allocations.SingleOrDefault(item =>
            item.ReferenceId == allocationReferenceId)
            ?? throw new InvalidOperationException("Refund allocation is missing from payment context.");
        var referenceType = allocation.ReferenceType == "BOOKING"
            ? OperatorLedgerReferenceType.BOOKING
            : OperatorLedgerReferenceType.PARCEL;
        var entryType = allocation.ReferenceType == "BOOKING"
            ? OperatorLedgerEntryType.BOOKING_REFUND
            : OperatorLedgerEntryType.PARCEL_REFUND;

        await _ledger.AddAsync(
            OperatorLedgerEntry.Create(
                allocation.OperatorId,
                allocation.TripId,
                entryType,
                -refundAmount,
                referenceType,
                allocation.ReferenceId,
                sourceEventId),
            cancellationToken);

        var paidAmount = checked(
            allocation.GrossAmount
            - allocation.VoucherVietRideFundedAmount
            - allocation.VoucherOperatorFundedAmount);
        var voucherReversal = paidAmount <= 0
            ? 0
            : Math.Min(
                allocation.VoucherVietRideFundedAmount,
                (long)Math.Floor(
                    (decimal)allocation.VoucherVietRideFundedAmount
                    * Math.Min(refundAmount, paidAmount)
                    / paidAmount));
        if (voucherReversal > 0)
        {
            await _ledger.AddAsync(
                OperatorLedgerEntry.Create(
                    allocation.OperatorId,
                    allocation.TripId,
                    OperatorLedgerEntryType.ADJUSTMENT,
                    -voucherReversal,
                    referenceType,
                    allocation.ReferenceId,
                    sourceEventId,
                    "reverse-vietride-funded-voucher"),
                cancellationToken);
        }
    }
}
