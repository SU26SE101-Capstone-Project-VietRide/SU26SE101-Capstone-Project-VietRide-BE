using VietRide.Payment.Application.Models;

namespace VietRide.Payment.Application.Abstractions.Services;

public interface IRevenueLedgerWriter
{
    Task RecordPaymentSucceededAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        CancellationToken cancellationToken);

    Task RecordRefundAsync(
        Guid sourceEventId,
        PaymentContextV1 context,
        Guid allocationReferenceId,
        long refundAmount,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

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
}
