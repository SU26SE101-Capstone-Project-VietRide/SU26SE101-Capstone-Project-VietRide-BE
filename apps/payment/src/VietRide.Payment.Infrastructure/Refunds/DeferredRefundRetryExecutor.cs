using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Refunds;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Refunds;

/// <summary>
/// Temporary Task 16.4 retry seam. Task 16.3 must replace this with the real wallet refund path.
/// </summary>
internal sealed class DeferredRefundRetryExecutor : IRefundRetryExecutor
{
    private const string DeferredReason = "Refund retry executor is not connected until Task 16.3 wires the wallet refund handler.";

    private readonly ILogger<DeferredRefundRetryExecutor> _logger;

    public DeferredRefundRetryExecutor(ILogger<DeferredRefundRetryExecutor> logger)
    {
        _logger = logger;
    }

    public Task<RefundRetryExecutionResult> ExecuteAsync(
        RefundFailureLog failure,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Refund retry for failure log {RefundFailureLogId} was deferred because Task 16.3 has not wired the wallet refund path.",
            failure.Id);

        return Task.FromResult(RefundRetryExecutionResult.Failure(DeferredReason));
    }
}
