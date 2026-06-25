using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Application.Abstractions.Refunds;

public interface IRefundRetryExecutor
{
    Task<RefundRetryExecutionResult> ExecuteAsync(
        RefundFailureLog failure,
        CancellationToken cancellationToken);
}
