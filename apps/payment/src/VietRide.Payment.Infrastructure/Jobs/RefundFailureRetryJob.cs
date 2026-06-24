using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Refunds;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Jobs;

/// <summary>
/// Recurring Hangfire entry point for retrying persisted refund failures.
/// </summary>
public sealed class RefundFailureRetryJob
{
    public const string RecurringJobId = "payment.refund-failure-retry";
    private const string RetryExhaustedCode = "REFUND_RETRY_EXHAUSTED";

    private readonly IRefundFailureLogRepository _refundFailures;
    private readonly IRefundRetryExecutor _retryExecutor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<RefundFailureRetryJob> _logger;

    public RefundFailureRetryJob(
        IRefundFailureLogRepository refundFailures,
        IRefundRetryExecutor retryExecutor,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<RefundFailureRetryJob> logger)
    {
        _refundFailures = refundFailures;
        _retryExecutor = retryExecutor;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Scope to retriable rows only (resolved_at IS NULL AND retry_count < max). Exhausted rows are
        // left unresolved for Admin manual handling (BSOT §5.9 REFUND_FAILURE_PERSISTED) and must NOT be
        // re-loaded here, otherwise the scan would re-surface REFUND_RETRY_EXHAUSTED on every run forever.
        var failures = await _refundFailures.GetRetryableAsync(RefundFailureLog.MaxRetryCount, cancellationToken);
        var now = _clock.UtcNow;
        var retriedCount = 0;

        foreach (var failure in failures)
        {
            if (!failure.CanRetry)
            {
                RecordRetryExhaustedIfNeeded(failure);
                SurfaceRetryExhausted(failure);
                continue;
            }

            var result = await _retryExecutor.ExecuteAsync(failure, cancellationToken);
            if (result.Succeeded)
            {
                failure.Resolve(now);
                continue;
            }

            retriedCount++;
            failure.RecordRetryFailure(now, result.FailureReason ?? "Refund retry failed.");

            if (failure.IsRetryExhausted)
            {
                failure.RecordRetryExhausted($"{RetryExhaustedCode}: {failure.FailureReason}");
                SurfaceRetryExhausted(failure);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (failures.Count > 0)
        {
            _logger.LogInformation(
                "Refund failure retry scan completed. Loaded {FailureCount} retryable failure log(s), retried {RetryCount}.",
                failures.Count,
                retriedCount);
        }
    }

    private void SurfaceRetryExhausted(RefundFailureLog failure)
    {
        _logger.LogError(
            "{ErrorCode} for refund failure log {RefundFailureLogId}. BookingId={BookingId}, ParcelId={ParcelId}, TriggerEventType={TriggerEventType}.",
            RetryExhaustedCode,
            failure.Id,
            failure.BookingId,
            failure.ParcelId,
            failure.TriggerEventType);
    }

    private static void RecordRetryExhaustedIfNeeded(RefundFailureLog failure)
    {
        if (!failure.FailureReason.StartsWith(RetryExhaustedCode, StringComparison.Ordinal))
        {
            failure.RecordRetryExhausted($"{RetryExhaustedCode}: {failure.FailureReason}");
        }
    }
}
