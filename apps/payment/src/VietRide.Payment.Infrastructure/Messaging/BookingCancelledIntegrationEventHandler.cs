using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Payment.Infrastructure.Refunds;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Infrastructure.Messaging;

public sealed class BookingCancelledIntegrationEventHandler
    : IIntegrationEventHandler<BookingCancelledIntegrationEvent>
{
    private const string BookingRefundReferenceType = "BOOKING_REFUND";
    private readonly ISender _sender;
    private readonly ILogger<BookingCancelledIntegrationEventHandler> _logger;
    private readonly RefundRetryService? _refunds;

    public BookingCancelledIntegrationEventHandler(
        ISender sender,
        ILogger<BookingCancelledIntegrationEventHandler> logger)
        : this(sender, logger, null)
    {
    }

    public BookingCancelledIntegrationEventHandler(
        ISender sender,
        ILogger<BookingCancelledIntegrationEventHandler> logger,
        RefundRetryService? refunds)
    {
        _sender = sender;
        _logger = logger;
        _refunds = refunds;
    }

    public async Task HandleAsync(
        BookingCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        integrationEvent.Validate();

        // Legacy facts are accepted for deserialization compatibility, but only the
        // canonical event (with eventId/occurredAt) may trigger a refund.
        if (!integrationEvent.HasEventId || !integrationEvent.HasOccurredAt)
        {
            _logger.LogWarning(
                "Ignoring legacy booking cancellation event for booking {BookingId}.",
                integrationEvent.BookingId);
            return;
        }

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

        if (_refunds is not null)
        {
            await _refunds.ExecuteBookingRefundAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _sender.Send(
                new RefundToWalletCommand(
                    integrationEvent.UserId!.Value,
                    integrationEvent.RefundAmount.Value,
                    BookingRefundReferenceType,
                    integrationEvent.BookingId!.Value,
                    integrationEvent.EventId!.Value.ToString("D")),
                cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Credited wallet refund for booking {BookingId} from {EventType}.",
            integrationEvent.BookingId,
            BookingCancelledIntegrationEvent.EventType);
    }
}
