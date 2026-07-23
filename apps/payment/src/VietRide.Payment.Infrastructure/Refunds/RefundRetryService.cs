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
/// Executes the first canonical booking-refund attempt and persists a retriable failure for Hangfire.
/// </summary>
public sealed class RefundRetryService
{
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

    public async Task<bool> ExecuteBookingRefundAsync(
        BookingCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var command = new RefundToWalletCommand(
            integrationEvent.UserId!.Value,
            integrationEvent.RefundAmount!.Value,
            "BOOKING_REFUND",
            integrationEvent.BookingId!.Value,
            (integrationEvent.EventId ?? integrationEvent.BookingId)!.Value.ToString("D"));

        try
        {
            await _sender.Send(command, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (_failures is null || _unitOfWork is null || _clock is null)
            {
                throw;
            }

            var reason = exception.Message;
            var now = _clock.UtcNow;
            var failure = RefundFailureLog.CreateForBookingRefund(
                integrationEvent.BookingId!.Value,
                integrationEvent.UserId!.Value,
                integrationEvent.RefundAmount!.Value,
                BookingCancelledIntegrationEvent.EventType,
                reason,
                now);

            await _failures.AddAsync(failure, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                exception,
                "Initial refund failed for booking {BookingId}; persisted retriable failure log {FailureId}.",
                integrationEvent.BookingId,
                failure.Id);
            return false;
        }
    }
}
