using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Payment.Infrastructure.Refunds;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Infrastructure.Messaging;

public sealed class BookingCancelledIntegrationEventHandler
    : IIntegrationEventHandler<BookingCancelledIntegrationEvent>
{
    private const string BookingRefundReferenceType = "BOOKING_REFUND";
    private const int ImmediateAttempts = 3;
    private readonly ISender _sender;
    private readonly ILogger<BookingCancelledIntegrationEventHandler> _logger;
    private readonly IServiceProvider? _services;

    public BookingCancelledIntegrationEventHandler(
        ISender sender,
        ILogger<BookingCancelledIntegrationEventHandler> logger)
        : this(sender, logger, null)
    {
    }

    public BookingCancelledIntegrationEventHandler(
        ISender sender,
        ILogger<BookingCancelledIntegrationEventHandler> logger,
        IServiceProvider? services)
    {
        _sender = sender;
        _logger = logger;
        _services = services;
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

        var refunds = _services?.GetService<RefundRetryService>();
        if (refunds is not null)
        {
            var refunded = await refunds.ExecuteBookingRefundAsync(
                integrationEvent,
                cancellationToken).ConfigureAwait(false);
            if (!refunded)
            {
                _logger.LogInformation(
                    "Deferred wallet refund for booking {BookingId} to the recurring retry job.",
                    integrationEvent.BookingId);
                return;
            }
        }
        else
        {
            var command = new RefundToWalletCommand(
                integrationEvent.UserId!.Value,
                integrationEvent.RefundAmount.Value,
                BookingRefundReferenceType,
                integrationEvent.BookingId!.Value,
                (integrationEvent.EventId ?? integrationEvent.BookingId)!.Value.ToString("D"));
            for (var attempt = 1; ; attempt++)
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
                catch (Exception exception) when (attempt < ImmediateAttempts)
                {
                    _logger.LogWarning(
                        exception,
                        "Wallet refund attempt {Attempt} failed for booking {BookingId}; retrying before acknowledgement.",
                        attempt,
                        integrationEvent.BookingId);
                }
            }
        }

        _logger.LogInformation(
            "Credited wallet refund for booking {BookingId} from {EventType}.",
            integrationEvent.BookingId,
            BookingCancelledIntegrationEvent.EventType);
    }
}
