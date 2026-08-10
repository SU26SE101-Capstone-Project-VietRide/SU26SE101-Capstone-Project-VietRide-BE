using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Time;

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
    private const string VnPayPayDateKey = "vnp_PayDate";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IVnPayClient _vnPayClient;
    private readonly IPaymentRepository _payments;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmBookingPaymentCommandHandler> _logger;
    private readonly IRevenueLedgerWriter _revenueLedger;

    public ConfirmBookingPaymentCommandHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments,
        IPlatformWalletRepository platformWallets,
        IIntegrationEventOutbox outbox,
        IRevenueLedgerWriter revenueLedger,
        IClock clock,
        ILogger<ConfirmBookingPaymentCommandHandler> logger)
    {
        _vnPayClient = vnPayClient;
        _payments = payments;
        _platformWallets = platformWallets;
        _outbox = outbox;
        _revenueLedger = revenueLedger;
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

        var paymentSnapshot = await FindBookingVnPayPaymentAsync(vnPayTxnRef, cancellationToken)
            .ConfigureAwait(false);
        if (paymentSnapshot is null)
        {
            _logger.LogWarning("VNPay booking payment IPN references unknown transaction {VnPayTxnRef}.", vnPayTxnRef);
            return OrderNotFound();
        }

        if (!TryValidateSignedPaymentFacts(
                request.Parameters,
                paymentSnapshot.Amount.Amount,
                out var amountValid,
                out var responseCode,
                out var transactionStatus,
                out var paidAt))
        {
            _logger.LogWarning(
                "VNPay booking payment IPN {VnPayTxnRef} has invalid signed payment facts.",
                vnPayTxnRef);
            return amountValid ? ConfirmFailure() : AmountInvalid();
        }

        var reservationAcquired = await _vnPayClient.TryReserveIpnAsync(vnPayTxnRef, cancellationToken)
            .ConfigureAwait(false);
        if (!reservationAcquired)
        {
            _logger.LogInformation("Skipping duplicate VNPay booking payment IPN for transaction {VnPayTxnRef}.", vnPayTxnRef);
            var currentPayment = await FindBookingVnPayPaymentAsync(vnPayTxnRef, cancellationToken)
                .ConfigureAwait(false);
            if (currentPayment is null)
                return OrderNotFound();

            return currentPayment.Status == PaymentStatus.PENDING_REDIRECT
                ? ConfirmFailure()
                : AlreadyProcessed();
        }

        try
        {
            var payment = await _payments.LockAndReloadAsync(paymentSnapshot.Id, cancellationToken)
                .ConfigureAwait(false);
            if (payment is null || !IsSupportedVnPayPayment(payment, vnPayTxnRef))
            {
                return OrderNotFound();
            }

            if (payment.Status is not (PaymentStatus.PENDING_REDIRECT or PaymentStatus.EXPIRED))
            {
                return AlreadyProcessed();
            }

            if (!string.Equals(responseCode, SuccessCode, StringComparison.Ordinal))
            {
                if (payment.Status == PaymentStatus.EXPIRED)
                {
                    return AlreadyProcessed();
                }

                await MarkFailedAsync(payment, responseCode, responseCode ?? "VNPay payment failed.", cancellationToken)
                    .ConfigureAwait(false);
                return ConfirmSuccess();
            }

            if (!string.Equals(transactionStatus, SuccessCode, StringComparison.Ordinal))
            {
                if (payment.Status == PaymentStatus.EXPIRED)
                {
                    return AlreadyProcessed();
                }

                await MarkFailedAsync(payment, transactionStatus, transactionStatus, cancellationToken).ConfigureAwait(false);
                return ConfirmSuccess();
            }

            payment.MarkSucceeded(responseCode, paidAt);
            _payments.Update(payment);

            var platformRef = MapHoldRef(payment.ReferenceType);
            await _platformWallets.CreditAsync(
                    payment.Amount,
                    platformRef,
                    payment.ReferenceId,
                    $"{payment.ReferenceType} payment hold",
                    cancellationToken)
                .ConfigureAwait(false);

            if (PaymentContextCodec.IsMissing(payment.Context))
            {
                payment.MarkContextReconciliationRequired();
                _payments.Update(payment);
                _logger.LogWarning(
                    "Confirmed legacy VNPay payment {PaymentId} without trusted context; settlement event is quarantined for reconciliation.",
                    payment.Id);
            }
            else
            {
                var effectiveDueAt = payment.DueAt ?? payment.CreatedAt.AddMinutes(15);
                await EnqueuePaymentSucceededAsync(
                    payment,
                    effectiveDueAt,
                    cancellationToken).ConfigureAwait(false);
                await EnqueueLateParcelRefundIfNeededAsync(
                    payment,
                    effectiveDueAt,
                    cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Confirmed VNPay payment {PaymentId} for reference {ReferenceType}/{ReferenceId} and credited platform hold {Amount} VND.",
                payment.Id,
                payment.ReferenceType,
                payment.ReferenceId,
                payment.Amount.Amount);

            return ConfirmSuccess();
        }
        finally
        {
            await _vnPayClient.ReleaseIpnReservationAsync(vnPayTxnRef, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task<VietRide.Payment.Domain.Entities.Payment?> FindBookingVnPayPaymentAsync(
        string vnPayTxnRef,
        CancellationToken cancellationToken)
    {
        var payment = await _payments.FindVnPayPaymentByTxnRefAsync(vnPayTxnRef, cancellationToken)
            .ConfigureAwait(false);
        return payment is not null && IsSupportedVnPayPayment(payment, vnPayTxnRef)
            ? payment
            : null;
    }

    private static bool IsSupportedVnPayPayment(
        VietRide.Payment.Domain.Entities.Payment payment,
        string vnPayTxnRef)
        => string.Equals(payment.VnPayTxnRef, vnPayTxnRef, StringComparison.Ordinal)
            && payment.Method == PaymentMethod.VNPAY
            && payment.ReferenceType is (
                PaymentReferenceType.BOOKING
                or PaymentReferenceType.BOOKING_GROUP
                or PaymentReferenceType.PARCEL
                or PaymentReferenceType.PARCEL_ADDITIONAL);

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
        DateTimeOffset effectiveDueAt,
        CancellationToken cancellationToken)
    {
        var context = PaymentContextCodec.DeserializeTrusted(payment.Context);
        var evt = new PaymentSucceededIntegrationEvent(
            payment.Id,
            payment.ReferenceType,
            payment.ReferenceId,
            payment.Amount.Amount,
            payment.Method,
            context,
            payment.SucceededAt!.Value,
            effectiveDueAt);
        await _revenueLedger.RecordPaymentSucceededAsync(
            evt.EventId,
            context,
            cancellationToken).ConfigureAwait(false);
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

    private async Task EnqueueLateParcelRefundIfNeededAsync(
        VietRide.Payment.Domain.Entities.Payment payment,
        DateTimeOffset effectiveDueAt,
        CancellationToken cancellationToken)
    {
        if (payment.ReferenceType is not (PaymentReferenceType.PARCEL or PaymentReferenceType.PARCEL_ADDITIONAL)
            || !payment.SucceededAt.HasValue
            || payment.SucceededAt.Value < effectiveDueAt
            || !payment.UserId.HasValue)
        {
            return;
        }

        var evt = new ParcelRefundInitiatedIntegrationEvent
        {
            ParcelId = payment.ReferenceId,
            SenderUserId = payment.UserId.Value,
            Amount = payment.Amount.Amount,
            ReferenceType = "PARCEL_REFUND",
            ReferenceId = payment.ReferenceId,
            IdempotencyKey = $"{payment.Id:D}:LATE_PAYMENT",
        };
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

    private static bool TryValidateSignedPaymentFacts(
        IReadOnlyDictionary<string, string> parameters,
        long paymentAmount,
        out bool amountValid,
        out string responseCode,
        out string transactionStatus,
        out DateTimeOffset paidAt)
    {
        amountValid = IsSignedAmountValid(parameters, paymentAmount);
        responseCode = string.Empty;
        transactionStatus = string.Empty;
        paidAt = default;

        if (!amountValid
            || !parameters.TryGetValue(VnPayResponseCodeKey, out var signedResponseCode)
            || string.IsNullOrWhiteSpace(signedResponseCode)
            || !parameters.TryGetValue(VnPayTransactionStatusKey, out var signedTransactionStatus)
            || string.IsNullOrWhiteSpace(signedTransactionStatus)
            || !parameters.TryGetValue(VnPayPayDateKey, out var payDate)
            || !DateTime.TryParseExact(
                payDate,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localPaidAt))
        {
            return false;
        }

        responseCode = signedResponseCode;
        transactionStatus = signedTransactionStatus;
        paidAt = BusinessTime.ToUtc(
            DateOnly.FromDateTime(localPaidAt),
            TimeOnly.FromDateTime(localPaidAt));
        return true;
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
