using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Infrastructure.Messaging;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Refunds;

/// <summary>
/// Executes the canonical booking refund and persists an exhausted retry for Hangfire.
/// </summary>
public sealed class RefundRetryService
{
    public const int MaxAttempts = 5;

    private readonly ISender _sender;
    private readonly IRefundFailureLogRepository? _failures;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly IClock? _clock;
    private readonly ILogger<RefundRetryService> _logger;

    public RefundRetryService(
        ISender sender,
        IRefundFailureLogRepository? failures,
        IUnitOfWork? unitOfWork,
        IClock? clock,
        ILogger<RefundRetryService> logger)
    {
        _sender = sender;
        _failures = failures;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteBookingRefundAsync(
        BookingCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var command = new RefundToWalletCommand(
            integrationEvent.UserId!.Value,
            integrationEvent.RefundAmount!.Value,
            "BOOKING_REFUND",
            integrationEvent.BookingId!.Value,
            integrationEvent.EventId!.Value.ToString("D"));

        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await _sender.Send(command, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < MaxAttempts)
                {
                    _logger.LogWarning(
                        ex,
                        "Wallet refund attempt {Attempt} failed for booking {BookingId}; retrying.",
                        attempt,
                        integrationEvent.BookingId);
                }
            }
        }

        if (_failures is null || _unitOfWork is null || _clock is null)
        {
            throw lastException ?? new InvalidOperationException("Wallet refund failed.");
        }

        var reason = lastException?.Message ?? "Wallet refund failed.";
        var now = _clock.UtcNow;
        var failure = RefundFailureLog.CreateForBookingRefund(
            integrationEvent.BookingId!.Value,
            integrationEvent.UserId!.Value,
            integrationEvent.RefundAmount!.Value,
            BookingCancelledIntegrationEvent.EventType,
            reason,
            now);
        for (var attempt = 0; attempt < RefundRetryService.MaxAttempts; attempt++)
        {
            failure.RecordRetryFailure(now, reason);
        }

        await _failures.AddAsync(failure, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogError(
            lastException,
            "Refund retries exhausted for booking {BookingId}; persisted failure log {FailureId}.",
            integrationEvent.BookingId,
            failure.Id);
    }
}
