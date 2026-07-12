using System.Text.Json;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Payments.ConfirmBookingPayment;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Payments.ConfirmSubscriptionPayment;

public sealed class ConfirmSubscriptionPaymentCommandHandler
    : IRequestHandler<ConfirmSubscriptionPaymentCommand, ConfirmBookingPaymentResult>
{
    private const string SuccessCode = "00";
    private readonly IVnPayClient _vnPayClient;
    private readonly IPaymentRepository _payments;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ConfirmSubscriptionPaymentCommandHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments,
        IPlatformWalletRepository platformWallets,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _vnPayClient = vnPayClient;
        _payments = payments;
        _platformWallets = platformWallets;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ConfirmBookingPaymentResult> Handle(
        ConfirmSubscriptionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_vnPayClient.VerifySignature(request.Parameters))
            return new ConfirmBookingPaymentResult("97", "PAYMENT_SIGNATURE_INVALID", 401);
        if (!request.Parameters.TryGetValue("vnp_TxnRef", out var txnRef) || string.IsNullOrWhiteSpace(txnRef))
            return OrderNotFound();

        var payment = _payments.Query().FirstOrDefault(candidate =>
            candidate.ReferenceType == PaymentReferenceType.SUBSCRIPTION
            && candidate.Method == PaymentMethod.VNPAY
            && candidate.VnPayTxnRef == txnRef);
        if (payment is null)
            return OrderNotFound();
        if (payment.Status != PaymentStatus.PENDING_REDIRECT)
            return ConfirmSuccess();

        await _payments.AcquirePaymentReferenceLockAsync(payment.ReferenceType, payment.ReferenceId, cancellationToken)
            .ConfigureAwait(false);
        payment = _payments.Query().FirstOrDefault(candidate =>
            candidate.Id == payment.Id && candidate.Status == PaymentStatus.PENDING_REDIRECT);
        if (payment is null)
            return ConfirmSuccess();

        if (!request.Parameters.TryGetValue("vnp_ResponseCode", out var responseCode)
            || !string.Equals(responseCode, SuccessCode, StringComparison.Ordinal)
            || !IsSignedAmountValid(request.Parameters, payment.Amount.Amount))
        {
            payment.MarkFailed(responseCode, _clock.UtcNow);
            _payments.Update(payment);
            return ConfirmSuccess();
        }

        payment.MarkSucceeded(responseCode, _clock.UtcNow);
        _payments.Update(payment);
        await _platformWallets.CreditAsync(
            payment.Amount,
            PlatformWalletTransactionRef.SUBSCRIPTION_PAYMENT,
            payment.ReferenceId,
            "Subscription VNPay payment",
            cancellationToken).ConfigureAwait(false);
        var evt = new SubscriptionPaymentSucceededIntegrationEvent(
            payment.Id,
            payment.ReferenceId,
            payment.OperatorId ?? Guid.Empty,
            payment.Amount.Amount);
        await _outbox.EnqueueAsync(evt.EventType, JsonSerializer.Serialize(evt), cancellationToken).ConfigureAwait(false);
        return ConfirmSuccess();
    }

    private static bool IsSignedAmountValid(IReadOnlyDictionary<string, string> parameters, long amount)
        => parameters.TryGetValue("vnp_Amount", out var signedAmount)
            && long.TryParse(signedAmount, out var parsed)
            && parsed == checked(amount * 100);

    private static ConfirmBookingPaymentResult ConfirmSuccess() => new(SuccessCode, "Confirm Success", 200);
    private static ConfirmBookingPaymentResult OrderNotFound() => new("01", "Order Not Found", 200);
}
