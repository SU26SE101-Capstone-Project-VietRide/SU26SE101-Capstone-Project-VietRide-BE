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
}
