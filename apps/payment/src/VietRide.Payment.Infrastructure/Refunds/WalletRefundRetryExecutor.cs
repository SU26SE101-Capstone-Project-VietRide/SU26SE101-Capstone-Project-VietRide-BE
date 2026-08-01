using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Refunds;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Refunds;

internal sealed class WalletRefundRetryExecutor : IRefundRetryExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WalletRefundRetryExecutor> _logger;

    public WalletRefundRetryExecutor(
        IServiceScopeFactory scopeFactory,
        ILogger<WalletRefundRetryExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<RefundRetryExecutionResult> ExecuteAsync(
        RefundFailureLog failure,
        CancellationToken cancellationToken)
    {
        if (!TryCreateCommand(failure, out var command, out var invalidReason))
        {
            return RefundRetryExecutionResult.Failure(invalidReason);
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(command, cancellationToken).ConfigureAwait(false);
            return RefundRetryExecutionResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Refund retry failed for refund failure log {RefundFailureLogId}.",
                failure.Id);

            return RefundRetryExecutionResult.Failure(ex.Message);
        }
    }

    private static bool TryCreateCommand(
        RefundFailureLog failure,
        out RefundToWalletCommand command,
        out string invalidReason)
    {
        command = default!;

        if (failure.UserId is null || failure.Amount is null || failure.ReferenceType is null || failure.ReferenceId is null)
        {
            invalidReason = "Refund failure log is missing retry payload.";
            return false;
        }

        if (failure.ReferenceType == "BOOKING_REFUND_PAYMENT")
        {
            if (!failure.BookingId.HasValue)
            {
                invalidReason = "Captured-payment refund failure log is missing its Booking allocation.";
                return false;
            }

            command = new RefundToWalletCommand(
                failure.UserId.Value,
                failure.Amount.Value,
                "BOOKING_REFUND",
                failure.BookingId.Value,
                $"refund-retry-{failure.Id:N}",
                failure.ReferenceId.Value);
        }
        else
        {
            command = new RefundToWalletCommand(
                failure.UserId.Value,
                failure.Amount.Value,
                failure.ReferenceType,
                failure.ReferenceId.Value,
                $"refund-retry-{failure.Id:N}");
        }
        invalidReason = string.Empty;
        return true;
    }
}
