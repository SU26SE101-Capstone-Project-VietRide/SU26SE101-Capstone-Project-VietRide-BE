using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Payments.ConfirmBookingPayment;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Payments.ConfirmSubscriptionPayment;

public sealed class ConfirmSubscriptionPaymentCommandHandler
    : IRequestHandler<ConfirmSubscriptionPaymentCommand, ConfirmBookingPaymentResult>
{
    private const string SuccessCode = "00";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IVnPayClient _vnPayClient;
    private readonly IPaymentRepository _payments;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmSubscriptionPaymentCommandHandler>? _logger;

    public ConfirmSubscriptionPaymentCommandHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments,
        IPlatformWalletRepository platformWallets,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<ConfirmSubscriptionPaymentCommandHandler>? logger = null)
    {
        _vnPayClient = vnPayClient;
        _payments = payments;
        _platformWallets = platformWallets;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ConfirmBookingPaymentResult> Handle(
        ConfirmSubscriptionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_vnPayClient.VerifySignature(request.Parameters))
            return new ConfirmBookingPaymentResult("97", "PAYMENT_SIGNATURE_INVALID", 200);
        if (!_vnPayClient.IsExpectedMerchant(request.Parameters))
            return new ConfirmBookingPaymentResult("99", "INVALID_MERCHANT", 200);
        if (!request.Parameters.TryGetValue("vnp_TxnRef", out var txnRef) || string.IsNullOrWhiteSpace(txnRef))
            return OrderNotFound();

        if (!await _vnPayClient.TryReserveIpnAsync(txnRef, cancellationToken).ConfigureAwait(false))
            return ConfirmSuccess();

        var payment = _payments.Query().FirstOrDefault(candidate =>
            candidate.ReferenceType == PaymentReferenceType.SUBSCRIPTION
            && candidate.Method == PaymentMethod.VNPAY
            && candidate.VnPayTxnRef == txnRef);
        if (payment is null)
        {
            await _vnPayClient.ReleaseIpnReservationAsync(txnRef, cancellationToken).ConfigureAwait(false);
            return OrderNotFound();
        }
        if (payment.Status != PaymentStatus.PENDING_REDIRECT)
            return ConfirmSuccess();

        await _payments.AcquirePaymentReferenceLockAsync(payment.ReferenceType, payment.ReferenceId, cancellationToken)
            .ConfigureAwait(false);
        payment = _payments.Query().FirstOrDefault(candidate =>
            candidate.Id == payment.Id && candidate.Status == PaymentStatus.PENDING_REDIRECT);
        if (payment is null)
            return ConfirmSuccess();

        var responseCode = request.Parameters.TryGetValue("vnp_ResponseCode", out var value) ? value : null;
        var transactionSucceeded = !request.Parameters.TryGetValue("vnp_TransactionStatus", out var transactionStatus)
            || string.Equals(transactionStatus, SuccessCode, StringComparison.Ordinal);
        if (!string.Equals(responseCode, SuccessCode, StringComparison.Ordinal)
            || !transactionSucceeded
            || !IsSignedAmountValid(request.Parameters, payment.Amount.Amount))
        {
            payment.MarkFailed(responseCode, _clock.UtcNow);
            _payments.Update(payment);
            await EnqueueFailedAsync(payment, responseCode, cancellationToken).ConfigureAwait(false);
            _logger?.LogWarning(
                "Subscription VNPay session {PaymentId} for attempt {UpgradeAttemptId} failed with response {ResponseCode}.",
                payment.Id,
                payment.ReferenceId,
                responseCode);
            return ConfirmSuccess();
        }

        var context = SubscriptionPaymentContextCodec.DeserializeTrusted(payment.Context);

        if (payment.DueAt.HasValue && _clock.UtcNow >= payment.DueAt.Value)
        {
            payment.MarkExpired(_clock.UtcNow);
            _payments.Update(payment);
            await EnqueueExpiredAsync(payment, context, cancellationToken).ConfigureAwait(false);
            _logger?.LogWarning(
                "Rejected late subscription VNPay success for payment {PaymentId}, attempt {UpgradeAttemptId}, due at {DueAt}.",
                payment.Id,
                payment.ReferenceId,
                payment.DueAt);
            return ConfirmSuccess();
        }

        payment.MarkSucceeded(responseCode, _clock.UtcNow);
        _payments.Update(payment);
        await _platformWallets.CreditAsync(
            payment.Amount,
            PlatformWalletTransactionRef.SUBSCRIPTION_PAYMENT,
            payment.Id,
            "Subscription VNPay payment",
            cancellationToken).ConfigureAwait(false);
        var evt = new SubscriptionPaymentSucceededIntegrationEvent(
            payment.Id,
            payment.ReferenceId,
            payment.OperatorId ?? Guid.Empty,
            context.OperatorSubscriptionId,
            payment.Amount.Amount,
            payment.Method.ToString(),
            payment.SucceededAt ?? _clock.UtcNow,
            context);
        await _outbox.EnqueueAsync(evt.EventType, JsonSerializer.Serialize(evt, JsonOptions), cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation(
            "Confirmed subscription VNPay payment {PaymentId} for attempt {UpgradeAttemptId}; event {EventId} queued.",
            payment.Id,
            payment.ReferenceId,
            evt.EventId);
        return ConfirmSuccess();
    }

    private Task EnqueueFailedAsync(
        VietRide.Payment.Domain.Entities.Payment payment,
        string? responseCode,
        CancellationToken cancellationToken)
    {
        var context = SubscriptionPaymentContextCodec.DeserializeTrusted(payment.Context);
        var evt = new SubscriptionPaymentFailedIntegrationEvent(
            payment.Id,
            payment.ReferenceId,
            payment.OperatorId ?? Guid.Empty,
            context.OperatorSubscriptionId,
            responseCode);
        return _outbox.EnqueueAsync(evt.EventType, JsonSerializer.Serialize(evt, JsonOptions), cancellationToken);
    }

    private Task EnqueueExpiredAsync(
        VietRide.Payment.Domain.Entities.Payment payment,
        SubscriptionPaymentContextV1 context,
        CancellationToken cancellationToken)
    {
        var evt = new SubscriptionPaymentExpiredIntegrationEvent(
            payment.Id,
            payment.ReferenceId,
            payment.OperatorId ?? Guid.Empty,
            context.OperatorSubscriptionId);
        return _outbox.EnqueueAsync(evt.EventType, JsonSerializer.Serialize(evt, JsonOptions), cancellationToken);
    }

    private static bool IsSignedAmountValid(IReadOnlyDictionary<string, string> parameters, long amount)
        => parameters.TryGetValue("vnp_Amount", out var signedAmount)
            && long.TryParse(signedAmount, out var parsed)
            && parsed == checked(amount * 100);

    private static ConfirmBookingPaymentResult ConfirmSuccess() => new(SuccessCode, "Confirm Success", 200);
    private static ConfirmBookingPaymentResult OrderNotFound() => new("01", "Order Not Found", 200);
}
