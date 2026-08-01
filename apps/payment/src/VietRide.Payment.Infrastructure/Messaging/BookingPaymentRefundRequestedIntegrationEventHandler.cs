using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure.Refunds;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Infrastructure.Messaging;

public sealed class BookingPaymentRefundRequestedIntegrationEventHandler
    : IIntegrationEventHandler<BookingPaymentRefundRequestedIntegrationEvent>
{
    private readonly IPaymentRepository _payments;
    private readonly RefundRetryService _refunds;
    private readonly ILogger<BookingPaymentRefundRequestedIntegrationEventHandler> _logger;

    public BookingPaymentRefundRequestedIntegrationEventHandler(
        IPaymentRepository payments,
        RefundRetryService refunds,
        ILogger<BookingPaymentRefundRequestedIntegrationEventHandler> logger)
    {
        _payments = payments;
        _refunds = refunds;
        _logger = logger;
    }

    public async Task HandleAsync(
        BookingPaymentRefundRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        integrationEvent.Validate();

        var payment = await _payments.GetByIdAsync(
            integrationEvent.PaymentId,
            cancellationToken).ConfigureAwait(false);
        if (payment is null
            || payment.Status is not (PaymentStatus.SUCCEEDED or PaymentStatus.REFUNDED)
            || payment.Method != PaymentMethod.VNPAY
            || !payment.UserId.HasValue
            || !string.Equals(
                payment.ReferenceType.ToString(),
                integrationEvent.PaymentReferenceType,
                StringComparison.Ordinal)
            || payment.ReferenceId != integrationEvent.PaymentReferenceId
            || payment.UserId.Value != integrationEvent.UserId
            || PaymentContextCodec.IsMissing(payment.Context))
        {
            throw new ArgumentException(
                "Booking payment-refund request does not match an authoritative captured VNPay payment.");
        }

        var context = PaymentContextCodec.DeserializeTrusted(payment.Context);
        var allocation = context.Allocations.SingleOrDefault(item =>
            item.ReferenceType == "BOOKING"
            && item.ReferenceId == integrationEvent.BookingId);
        if (allocation is null)
        {
            throw new ArgumentException(
                "Booking payment-refund request has no matching trusted allocation.");
        }

        long trustedAmount;
        try
        {
            trustedAmount = checked(
                allocation.GrossAmount
                - allocation.VoucherVietRideFundedAmount
                - allocation.VoucherOperatorFundedAmount);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException(
                "Booking payment-refund allocation amount is invalid.",
                exception);
        }

        if (trustedAmount < 0 || trustedAmount != integrationEvent.Amount)
        {
            throw new ArgumentException(
                "Booking payment-refund request amount does not match the trusted allocation.");
        }

        var refunded = await _refunds.ExecuteBookingRefundAsync(
            integrationEvent.BookingId,
            payment.UserId.Value,
            trustedAmount,
            payment.Id,
            integrationEvent.EventId,
            BookingPaymentRefundRequestedIntegrationEvent.EventType,
            cancellationToken).ConfigureAwait(false);
        if (!refunded)
        {
            _logger.LogInformation(
                "Deferred captured-payment refund for booking {BookingId} to the recurring retry job.",
                integrationEvent.BookingId);
            return;
        }

        _logger.LogInformation(
            "Credited trusted captured-payment refund for booking {BookingId} from payment {PaymentId}.",
            integrationEvent.BookingId,
            payment.Id);
    }
}
