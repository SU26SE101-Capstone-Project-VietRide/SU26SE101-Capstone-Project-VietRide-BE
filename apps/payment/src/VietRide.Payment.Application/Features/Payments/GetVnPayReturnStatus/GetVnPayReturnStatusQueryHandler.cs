using System.Globalization;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Payments.GetVnPayReturnStatus;

public sealed class GetVnPayReturnStatusQueryHandler
    : IRequestHandler<GetVnPayReturnStatusQuery, VnPayReturnStatusResponse>
{
    private readonly IVnPayClient _vnPayClient;
    private readonly IPaymentRepository _payments;

    public GetVnPayReturnStatusQueryHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments)
    {
        _vnPayClient = vnPayClient;
        _payments = payments;
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

        return new VnPayReturnStatusResponse(
            txnRef,
            payment.Id,
            payment.ReferenceType.ToString(),
            payment.ReferenceId,
            payment.Status.ToString());
    }
}
