using System.Globalization;
using System.Text.Json;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Payments.GetVnPayReturnStatus;

public sealed class GetVnPayReturnStatusQueryHandler
    : IRequestHandler<GetVnPayReturnStatusQuery, VnPayReturnStatusResponse>
{
    private const string VnPayCustomerCancelledCode = "24";
    private const string VnPaySucceededCode = "00";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IVnPayClient _vnPayClient;
    private readonly IPaymentRepository _payments;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public GetVnPayReturnStatusQueryHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _vnPayClient = vnPayClient;
        _payments = payments;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<VnPayReturnStatusResponse> Handle(
        GetVnPayReturnStatusQuery request,
        CancellationToken cancellationToken)
    {
        if (!_vnPayClient.VerifySignature(request.Parameters)
            || !_vnPayClient.IsExpectedMerchant(request.Parameters))
        {
            throw new UnauthorizedException(
                "PAYMENT_SIGNATURE_INVALID",
                "VNPay return parameters are not authentic.");
        }

        if (!request.Parameters.TryGetValue("vnp_TxnRef", out var txnRef)
            || string.IsNullOrWhiteSpace(txnRef))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "vnp_TxnRef is required.");
        }

        var payment = await _payments.FindVnPayPaymentByTxnRefAsync(txnRef, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CodedNotFoundException(
                "PAYMENT_NOT_FOUND",
                "Payment was not found.");

        if (payment.ReturnMode != VnPayReturnMode.OPERATOR_WEB)
        {
            throw new CodedNotFoundException(
                "PAYMENT_NOT_FOUND",
                "Payment was not found.");
        }

        if (!request.Parameters.TryGetValue("vnp_Amount", out var rawAmount)
            || !long.TryParse(rawAmount, NumberStyles.None, CultureInfo.InvariantCulture, out var providerAmount)
            || providerAmount <= 0
            || providerAmount % 100 != 0
            || providerAmount / 100 != payment.Amount.Amount)
        {
            throw new CodedValidationException(
                "PAYMENT_AMOUNT_INVALID",
                "VNPay return amount does not match the payment session.");
        }

        payment = await MarkSignedSubscriptionCancellationAsync(
                payment,
                request.Parameters,
                cancellationToken)
            .ConfigureAwait(false);

        return new VnPayReturnStatusResponse(
            txnRef,
            payment.Id,
            payment.ReferenceType.ToString(),
            payment.ReferenceId,
            payment.Status.ToString());
    }

    private async Task<VietRide.Payment.Domain.Entities.Payment> MarkSignedSubscriptionCancellationAsync(
        VietRide.Payment.Domain.Entities.Payment payment,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        parameters.TryGetValue("vnp_ResponseCode", out var responseCode);
        parameters.TryGetValue("vnp_TransactionStatus", out var transactionStatus);
        if (payment.ReferenceType != PaymentReferenceType.SUBSCRIPTION
            || !string.Equals(responseCode, VnPayCustomerCancelledCode, StringComparison.Ordinal)
            || string.Equals(transactionStatus, VnPaySucceededCode, StringComparison.Ordinal))
        {
            return payment;
        }

        await _payments.AcquirePaymentReferenceLockAsync(
                payment.ReferenceType,
                payment.ReferenceId,
                cancellationToken)
            .ConfigureAwait(false);

        var lockedPayment = await _payments.LockAndReloadAsync(payment.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CodedNotFoundException(
                "PAYMENT_NOT_FOUND",
                "Payment was not found.");
        if (lockedPayment.Status != PaymentStatus.PENDING_REDIRECT)
            return lockedPayment;

        var context = SubscriptionPaymentContextCodec.DeserializeTrusted(lockedPayment.Context);
        lockedPayment.MarkFailed(VnPayCustomerCancelledCode, _clock.UtcNow);
        _payments.Update(lockedPayment);

        var integrationEvent = new SubscriptionPaymentFailedIntegrationEvent(
            lockedPayment.Id,
            lockedPayment.ReferenceId,
            lockedPayment.OperatorId ?? Guid.Empty,
            context.OperatorSubscriptionId,
            VnPayCustomerCancelledCode);
        await _outbox.EnqueueAsync(
                integrationEvent.EventType,
                JsonSerializer.Serialize(integrationEvent, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);

        return lockedPayment;
    }
}
