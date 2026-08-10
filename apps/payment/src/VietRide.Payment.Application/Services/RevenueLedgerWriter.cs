using System.Security.Cryptography;
using System.Text;
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

    public Task RecordPaymentSucceededAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        CancellationToken cancellationToken)
        => RecordPaymentSucceededAsync(sourceEventId, context, DateTimeOffset.UtcNow, cancellationToken);

    public async Task RecordPaymentSucceededAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        DateTimeOffset occurredAt,
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
                        sourceEventId,
                        referenceCode: allocation.ReferenceCode,
                        occurredAt: occurredAt),
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
                        sourceEventId,
                        referenceCode: allocation.ReferenceCode,
                        occurredAt: occurredAt),
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
                        "operator-funded-voucher",
                        referenceCode: allocation.ReferenceCode,
                        occurredAt: occurredAt,
                        operatorFundedVoucherAmount: allocation.VoucherOperatorFundedAmount),
                    cancellationToken);
            }
        }
    }

    public Task RecordRefundAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        CancellationToken cancellationToken)
        => RecordRefundAsync(
            sourceEventId,
            context,
            allocationReferenceId,
            refundAmount,
            DateTimeOffset.UtcNow,
            cancellationToken);

    public async Task RecordRefundAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (refundAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(refundAmount));

        var allocation = context.Allocations.SingleOrDefault(item =>
            item.ReferenceId == allocationReferenceId)
            ?? throw new InvalidOperationException("Refund allocation is missing from payment context.");
        if (await IsRefundRecordedAsync(
            sourceEventId,
            allocationReferenceId,
            cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var referenceType = allocation.ReferenceType == "BOOKING"
            ? OperatorLedgerReferenceType.BOOKING
            : OperatorLedgerReferenceType.PARCEL;
        var entryType = allocation.ReferenceType == "BOOKING"
            ? OperatorLedgerEntryType.BOOKING_REFUND
            : OperatorLedgerEntryType.PARCEL_REFUND;

        if (refundAmount > 0)
        {
            await _ledger.AddAsync(
                OperatorLedgerEntry.Create(
                    allocation.OperatorId,
                    allocation.TripId,
                    entryType,
                    -refundAmount,
                    referenceType,
                    allocation.ReferenceId,
                    sourceEventId,
                    referenceCode: allocation.ReferenceCode,
                    occurredAt: occurredAt),
                cancellationToken);
        }

        if (allocation.VoucherVietRideFundedAmount > 0)
        {
            var adjustmentSourceEventId = allocation.ReferenceType == "PARCEL"
                ? CreateParcelVoucherAdjustmentSourceId(sourceEventId, allocation.ReferenceId)
                : sourceEventId;
            await _ledger.AddAsync(
                OperatorLedgerEntry.Create(
                    allocation.OperatorId,
                    allocation.TripId,
                    OperatorLedgerEntryType.ADJUSTMENT,
                    -allocation.VoucherVietRideFundedAmount,
                    referenceType,
                    allocation.ReferenceId,
                    adjustmentSourceEventId,
                    "reverse-vietride-funded-voucher",
                    adjustmentReason: OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL,
                    referenceCode: allocation.ReferenceCode,
                    occurredAt: occurredAt),
                cancellationToken);
        }
    }

    public Task<bool> IsRefundRecordedAsync(
        Guid sourceEventId,
        Guid allocationReferenceId,
        CancellationToken cancellationToken)
        => _ledger.HasSourceEntryAsync(
            sourceEventId,
            allocationReferenceId,
            cancellationToken);

    public Task RecordGenericBookingRefundEntitlementAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        CancellationToken cancellationToken)
        => RecordGenericBookingRefundEntitlementAsync(
            sourceEventId,
            context,
            allocationReferenceId,
            DateTimeOffset.UtcNow,
            cancellationToken);

    public async Task RecordGenericBookingRefundEntitlementAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var allocation = context.Allocations.SingleOrDefault(item =>
            item.ReferenceType == "BOOKING" && item.ReferenceId == allocationReferenceId)
            ?? throw new InvalidOperationException("Booking refund allocation is missing from payment context.");
        if (await IsRefundRecordedAsync(
            sourceEventId,
            allocationReferenceId,
            cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _ledger.AddAsync(
            OperatorLedgerEntry.Create(
                allocation.OperatorId,
                allocation.TripId,
                OperatorLedgerEntryType.ADJUSTMENT,
                0,
                OperatorLedgerReferenceType.BOOKING,
                allocation.ReferenceId,
                sourceEventId,
                "generic-booking-refund-entitlement",
                adjustmentReason: OperatorLedgerAdjustmentReason.GENERIC_BOOKING_REFUND_ENTITLEMENT,
                referenceCode: allocation.ReferenceCode,
                occurredAt: occurredAt),
            cancellationToken).ConfigureAwait(false);
    }

    public Task RecordCorrelatedBookingRefundAsync(
        Guid sourceEventId,
        Guid voucherAdjustmentSourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        CancellationToken cancellationToken)
        => RecordCorrelatedBookingRefundAsync(
            sourceEventId,
            voucherAdjustmentSourceEventId,
            context,
            allocationReferenceId,
            refundAmount,
            DateTimeOffset.UtcNow,
            cancellationToken);

    public async Task RecordCorrelatedBookingRefundAsync(
        Guid sourceEventId,
        Guid voucherAdjustmentSourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (refundAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(refundAmount));

        var allocation = context.Allocations.SingleOrDefault(item =>
            item.ReferenceType == "BOOKING" && item.ReferenceId == allocationReferenceId)
            ?? throw new InvalidOperationException("Booking refund allocation is missing from payment context.");
        if (refundAmount > 0
            && !await IsRefundRecordedAsync(
                sourceEventId,
                allocationReferenceId,
                cancellationToken).ConfigureAwait(false))
        {
            await _ledger.AddAsync(
                OperatorLedgerEntry.Create(
                    allocation.OperatorId,
                    allocation.TripId,
                    OperatorLedgerEntryType.BOOKING_REFUND,
                    -refundAmount,
                    OperatorLedgerReferenceType.BOOKING,
                    allocation.ReferenceId,
                    sourceEventId,
                    referenceCode: allocation.ReferenceCode,
                    occurredAt: occurredAt),
                cancellationToken).ConfigureAwait(false);
        }

        if (allocation.VoucherVietRideFundedAmount > 0
            && !await IsRefundRecordedAsync(
                voucherAdjustmentSourceEventId,
                allocationReferenceId,
                cancellationToken).ConfigureAwait(false))
        {
            await _ledger.AddAsync(
                OperatorLedgerEntry.Create(
                    allocation.OperatorId,
                    allocation.TripId,
                    OperatorLedgerEntryType.ADJUSTMENT,
                    -allocation.VoucherVietRideFundedAmount,
                    OperatorLedgerReferenceType.BOOKING,
                    allocation.ReferenceId,
                    voucherAdjustmentSourceEventId,
                    "reverse-vietride-funded-voucher",
                    adjustmentReason: OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL,
                    referenceCode: allocation.ReferenceCode,
                    occurredAt: occurredAt),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public static Guid CreateParcelVoucherAdjustmentSourceId(
        Guid originalParcelRefundSourceEventId,
        Guid parcelId)
    {
        var source = string.Concat(
            "parcel-refund-voucher-adjustment:",
            originalParcelRefundSourceEventId,
            ":allocation:",
            parcelId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return new Guid(hash.AsSpan(0, 16));
    }
}
