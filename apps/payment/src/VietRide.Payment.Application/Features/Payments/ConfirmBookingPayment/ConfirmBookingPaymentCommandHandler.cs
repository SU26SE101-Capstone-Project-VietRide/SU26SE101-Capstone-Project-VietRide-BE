using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Payments.ConfirmBookingPayment;

public sealed class ConfirmBookingPaymentCommandHandler
    : IRequestHandler<ConfirmBookingPaymentCommand, ConfirmBookingPaymentResult>
{
    private const string SuccessCode = "00";
    private const string SignatureInvalidCode = "PAYMENT_SIGNATURE_INVALID";
    private const string VnPayTxnRefKey = "vnp_TxnRef";
    private const string VnPayResponseCodeKey = "vnp_ResponseCode";
    private const string VnPayAmountKey = "vnp_Amount";
    private const string VnPayTransactionStatusKey = "vnp_TransactionStatus";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IVnPayClient _vnPayClient;
    private readonly IPaymentRepository _payments;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmBookingPaymentCommandHandler> _logger;

    public ConfirmBookingPaymentCommandHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments,
        IPlatformWalletRepository platformWallets,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<ConfirmBookingPaymentCommandHandler> logger)
    {
        _vnPayClient = vnPayClient;
        _payments = payments;
        _platformWallets = platformWallets;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ConfirmBookingPaymentResult> Handle(
        ConfirmBookingPaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_vnPayClient.VerifySignature(request.Parameters))
        {
            return new ConfirmBookingPaymentResult("97", SignatureInvalidCode, 200);
        }

        if (!_vnPayClient.IsExpectedMerchant(request.Parameters))
        {
            return new ConfirmBookingPaymentResult("99", "INVALID_MERCHANT", 200);
        }

        if (!request.Parameters.TryGetValue(VnPayTxnRefKey, out var vnPayTxnRef)
            || string.IsNullOrWhiteSpace(vnPayTxnRef))
        {
            return OrderNotFound();
        }

        var reservationAcquired = await _vnPayClient.TryReserveIpnAsync(vnPayTxnRef, cancellationToken)
            .ConfigureAwait(false);
        if (!reservationAcquired)
        {
            _logger.LogInformation("Skipping duplicate VNPay booking payment IPN for transaction {VnPayTxnRef}.", vnPayTxnRef);
            return AlreadyProcessed();
        }

        var shouldReleaseReservation = true;

        try
        {
            var payment = FindBookingVnPayPayment(vnPayTxnRef, pendingOnly: true);
            if (payment is null)
            {
                var currentPayment = FindBookingVnPayPayment(vnPayTxnRef, pendingOnly: false);
                if (currentPayment is null)
                {
                    _logger.LogWarning("VNPay booking payment IPN references unknown transaction {VnPayTxnRef}.", vnPayTxnRef);
                    return OrderNotFound();
                }

                return AlreadyProcessed();
            }

            await _payments.AcquirePaymentReferenceLockAsync(
                    payment.ReferenceType,
                    payment.ReferenceId,
                    cancellationToken)
                .ConfigureAwait(false);

            payment = FindBookingVnPayPayment(vnPayTxnRef, pendingOnly: true);
            if (payment is null)
            {
                return ConfirmSuccess();
            }

            var responseCode = request.Parameters.TryGetValue(VnPayResponseCodeKey, out var value)
                ? value
                : null;

            if (!string.Equals(responseCode, SuccessCode, StringComparison.Ordinal))
            {
                await MarkFailedAsync(payment, responseCode, responseCode ?? "VNPay payment failed.", cancellationToken)
                    .ConfigureAwait(false);
                shouldReleaseReservation = false;
                return ConfirmSuccess();
            }

            if (!IsSignedAmountValid(request.Parameters, payment.Amount.Amount))
            {
                _logger.LogWarning(
                    "VNPay booking payment IPN {VnPayTxnRef} has missing, invalid, or mismatched amount.",
                    vnPayTxnRef);
                await MarkFailedAsync(payment, responseCode, "AMOUNT_MISMATCH", cancellationToken)
                    .ConfigureAwait(false);
                return AmountInvalid();
            }

            if (!request.Parameters.TryGetValue(VnPayTransactionStatusKey, out var transactionStatus)
                || !string.Equals(transactionStatus, SuccessCode, StringComparison.Ordinal))
            {
                await MarkFailedAsync(payment, transactionStatus, transactionStatus ?? "MISSING_TRANSACTION_STATUS", cancellationToken)
                    .ConfigureAwait(false);
                shouldReleaseReservation = false;
                return ConfirmSuccess();
            }

            payment.MarkSucceeded(responseCode, _clock.UtcNow);
            _payments.Update(payment);

            var platformRef = MapHoldRef(payment.ReferenceType);
            await _platformWallets.CreditAsync(
                    payment.Amount,
                    platformRef,
                    payment.ReferenceId,
                    $"{payment.ReferenceType} payment hold",
                    cancellationToken)
                .ConfigureAwait(false);

            await EnqueuePaymentSucceededAsync(payment, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Confirmed VNPay payment {PaymentId} for reference {ReferenceType}/{ReferenceId} and credited platform hold {Amount} VND.",
                payment.Id,
                payment.ReferenceType,
                payment.ReferenceId,
                payment.Amount.Amount);

            shouldReleaseReservation = false;
            return ConfirmSuccess();
        }
        finally
        {
            if (shouldReleaseReservation)
            {
                await _vnPayClient.ReleaseIpnReservationAsync(vnPayTxnRef, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private VietRide.Payment.Domain.Entities.Payment? FindBookingVnPayPayment(string vnPayTxnRef, bool pendingOnly)
    {
        var query = _payments.Query()
            .Where(payment =>
                payment.VnPayTxnRef == vnPayTxnRef
                && payment.Method == PaymentMethod.VNPAY
                && (payment.ReferenceType == PaymentReferenceType.BOOKING
                    || payment.ReferenceType == PaymentReferenceType.BOOKING_GROUP
                    || payment.ReferenceType == PaymentReferenceType.PARCEL
                    || payment.ReferenceType == PaymentReferenceType.PARCEL_ADDITIONAL));

        if (pendingOnly)
        {
            query = query.Where(payment => payment.Status == PaymentStatus.PENDING_REDIRECT);
        }

        return query.FirstOrDefault();
    }

    private async Task MarkFailedAsync(
        VietRide.Payment.Domain.Entities.Payment payment,
        string? vnPayResponseCode,
        string reason,
        CancellationToken cancellationToken)
    {
        payment.MarkFailed(vnPayResponseCode, _clock.UtcNow);
        _payments.Update(payment);
        await EnqueuePaymentFailedAsync(payment, reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnqueuePaymentSucceededAsync(
        VietRide.Payment.Domain.Entities.Payment payment,
        CancellationToken cancellationToken)
    {
        var evt = new PaymentSucceededIntegrationEvent(
            payment.Id,
            payment.ReferenceType,
            payment.ReferenceId,
            payment.Amount.Amount);
        var payload = JsonSerializer.Serialize(evt, JsonOptions);
        await _outbox.EnqueueAsync(evt.EventType, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnqueuePaymentFailedAsync(
        VietRide.Payment.Domain.Entities.Payment payment,
        string reason,
        CancellationToken cancellationToken)
    {
        var evt = new PaymentFailedIntegrationEvent(
            payment.Id,
            payment.ReferenceType,
            payment.ReferenceId,
            reason);
        var payload = JsonSerializer.Serialize(evt, JsonOptions);
        await _outbox.EnqueueAsync(evt.EventType, payload, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSignedAmountValid(IReadOnlyDictionary<string, string> parameters, long paymentAmount)
    {
        if (!parameters.TryGetValue(VnPayAmountKey, out var signedAmount)
            || !long.TryParse(signedAmount, out var parsedSignedAmount))
        {
            return false;
        }

        try
        {
            return parsedSignedAmount == checked(paymentAmount * 100);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static ConfirmBookingPaymentResult ConfirmSuccess()
        => new(SuccessCode, "Confirm Success", 200);

    private static ConfirmBookingPaymentResult ConfirmFailure()
        => new("99", "Confirm Failed", 200);

    private static ConfirmBookingPaymentResult AlreadyProcessed()
        => new("02", "Order Already Confirmed", 200);

    private static ConfirmBookingPaymentResult AmountInvalid()
        => new("04", "Invalid Amount", 200);

    private static ConfirmBookingPaymentResult OrderNotFound()
        => new("01", "Order Not Found", 200);

    private static PlatformWalletTransactionRef MapHoldRef(PaymentReferenceType referenceType)
        => referenceType switch
        {
            PaymentReferenceType.BOOKING or PaymentReferenceType.BOOKING_GROUP
                => PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD,
            PaymentReferenceType.PARCEL
                => PlatformWalletTransactionRef.PARCEL_PAYMENT_HOLD,
            PaymentReferenceType.PARCEL_ADDITIONAL
                => PlatformWalletTransactionRef.PARCEL_ADDITIONAL_PAYMENT_HOLD,
            _ => PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD,
        };
}
