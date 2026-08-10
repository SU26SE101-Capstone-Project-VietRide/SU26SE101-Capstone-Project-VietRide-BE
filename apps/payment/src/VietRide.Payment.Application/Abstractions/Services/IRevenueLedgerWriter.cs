using VietRide.Payment.Application.Models;

namespace VietRide.Payment.Application.Abstractions.Services;

public interface IRevenueLedgerWriter
{
    Task RecordPaymentSucceededAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        CancellationToken cancellationToken);

    Task RecordPaymentSucceededAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
        => RecordPaymentSucceededAsync(sourceEventId, context, cancellationToken);

    Task RecordRefundAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    Task RecordRefundAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
        => RecordRefundAsync(sourceEventId, context, allocationReferenceId, refundAmount, cancellationToken);

    Task<bool> IsRefundRecordedAsync(
        Guid sourceEventId,
        Guid allocationReferenceId,
        CancellationToken cancellationToken)
        => Task.FromResult(false);

    Task RecordGenericBookingRefundEntitlementAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    Task RecordGenericBookingRefundEntitlementAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
        => RecordGenericBookingRefundEntitlementAsync(
            sourceEventId,
            context,
            allocationReferenceId,
            cancellationToken);

    Task RecordCorrelatedBookingRefundAsync(
        Guid sourceEventId,
        Guid voucherAdjustmentSourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        CancellationToken cancellationToken)
        => RecordRefundAsync(
            sourceEventId,
            context,
            allocationReferenceId,
            refundAmount,
            cancellationToken);

    Task RecordCorrelatedBookingRefundAsync(
        Guid sourceEventId,
        Guid voucherAdjustmentSourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
        => RecordCorrelatedBookingRefundAsync(
            sourceEventId,
            voucherAdjustmentSourceEventId,
            context,
            allocationReferenceId,
            refundAmount,
            cancellationToken);
}
