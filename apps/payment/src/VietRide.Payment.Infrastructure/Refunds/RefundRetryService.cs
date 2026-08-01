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
    private readonly PaymentDbContext? _dbContext;

    public RefundRetryService(
        ISender sender,
        IRefundFailureLogRepository? failures,
        IUnitOfWork? unitOfWork,
        IClock? clock,
        ILogger<RefundRetryService> logger,
        PaymentDbContext? dbContext = null)
    {
        _sender = sender;
        _failures = failures;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task<bool> ExecuteBookingRefundAsync(
        BookingCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        => await ExecuteBookingRefundCoreAsync(
            integrationEvent.BookingId!.Value,
            integrationEvent.UserId!.Value,
            integrationEvent.RefundAmount!.Value,
            paymentId: null,
            (integrationEvent.EventId ?? integrationEvent.BookingId)!.Value,
            BookingCancelledIntegrationEvent.EventType,
            cancellationToken).ConfigureAwait(false);

    public async Task<bool> ExecuteBookingRefundAsync(
        Guid bookingId,
        Guid userId,
        long amount,
        Guid paymentId,
        Guid sourceEventId,
        string sourceEventType,
        CancellationToken cancellationToken)
        => await ExecuteBookingRefundCoreAsync(
            bookingId,
            userId,
            amount,
            paymentId,
            sourceEventId,
            sourceEventType,
            cancellationToken).ConfigureAwait(false);

    private async Task<bool> ExecuteBookingRefundCoreAsync(
        Guid bookingId,
        Guid userId,
        long amount,
        Guid? paymentId,
        Guid sourceEventId,
        string sourceEventType,
        CancellationToken cancellationToken)
    {
        var command = new RefundToWalletCommand(
            userId,
            amount,
            "BOOKING_REFUND",
            bookingId,
            sourceEventId.ToString("D"),
            paymentId);
        var transaction = _dbContext?.Database.CurrentTransaction;
        var savepointName = $"captured_refund_{sourceEventId:N}";

        if (transaction is not null)
        {
            await transaction.CreateSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await _sender.Send(command, cancellationToken).ConfigureAwait(false);
            if (transaction is not null)
            {
                await transaction.ReleaseSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
                await transaction.ReleaseSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
            }

            _dbContext?.ChangeTracker.Clear();

            if (_failures is null || _unitOfWork is null || _clock is null)
            {
                throw;
            }

            var reason = exception.Message;
            var now = _clock.UtcNow;
            var failure = paymentId.HasValue
                ? RefundFailureLog.CreateForBookingRefund(
                    bookingId,
                    userId,
                    amount,
                    paymentId.Value,
                    sourceEventType,
                    reason,
                    now)
                : RefundFailureLog.CreateForBooking(
                    bookingId,
                    sourceEventType,
                    reason,
                    now,
                    userId,
                    amount,
                    "BOOKING_REFUND",
                    bookingId);

            await _failures.AddAsync(failure, cancellationToken).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                exception,
                "Initial refund failed for booking {BookingId}; persisted retriable failure log {FailureId}.",
                bookingId,
                failure.Id);
            return false;
        }
    }
}
