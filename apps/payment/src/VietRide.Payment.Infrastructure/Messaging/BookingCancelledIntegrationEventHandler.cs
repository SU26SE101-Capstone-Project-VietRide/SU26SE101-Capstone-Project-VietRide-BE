using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Infrastructure.Messaging;

public sealed class BookingCancelledIntegrationEventHandler
    : IIntegrationEventHandler<BookingCancelledIntegrationEvent>
{
    private const string BookingRefundReferenceType = "BOOKING_REFUND";
    private const int MaxAttempts = 3;

    private readonly ISender _sender;
    private readonly ILogger<BookingCancelledIntegrationEventHandler> _logger;

    public BookingCancelledIntegrationEventHandler(
        ISender sender,
        ILogger<BookingCancelledIntegrationEventHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        BookingCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        integrationEvent.Validate();

        // A PENDING_PAYMENT cancellation carries no paid money, so refundAmount is 0.
        // RefundToWalletCommandValidator requires Amount > 0; sending it would dead-letter the
        // event. A 0-VND refund is a no-op — the booking correctly stays CANCELLED (never REFUNDED).
        if (integrationEvent.RefundAmount!.Value <= 0)
        {
            _logger.LogInformation(
                "Skipping wallet refund for booking {BookingId}: refund amount is 0.",
                integrationEvent.BookingId);
            return;
        }

        var command = new RefundToWalletCommand(
            integrationEvent.UserId!.Value,
            integrationEvent.RefundAmount.Value,
            BookingRefundReferenceType,
            integrationEvent.BookingId!.Value,
            (integrationEvent.EventId ?? integrationEvent.BookingId)!.Value.ToString("D"));

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await _sender.Send(command, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Wallet refund attempt {Attempt} failed for booking {BookingId}; retrying.",
                    attempt,
                    integrationEvent.BookingId);
            }
        }

        _logger.LogInformation(
            "Credited wallet refund for booking {BookingId} from {EventType}.",
            integrationEvent.BookingId,
            BookingCancelledIntegrationEvent.EventType);
    }
}
