using System.Globalization;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Features.Payments.GetVnPayMobileSdkReturn;

public sealed class GetVnPayMobileSdkReturnQueryHandler
    : IRequestHandler<GetVnPayMobileSdkReturnQuery, VnPayMobileSdkReturnResult>
{
    private const string SuccessUri = "http://success.sdk.merchantbackapp";
    private const string CancelUri = "http://cancel.sdk.merchantbackapp";
    private const string FailUri = "http://fail.sdk.merchantbackapp";

    private readonly IVnPayClient _vnPayClient;
    private readonly IPaymentRepository _payments;
    private readonly ITopUpRequestRepository _topUpRequests;

    public GetVnPayMobileSdkReturnQueryHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments,
        ITopUpRequestRepository topUpRequests)
    {
        _vnPayClient = vnPayClient;
        _payments = payments;
        _topUpRequests = topUpRequests;
    }

    public async Task<VnPayMobileSdkReturnResult> Handle(
        GetVnPayMobileSdkReturnQuery request,
        CancellationToken cancellationToken)
    {
        if (!_vnPayClient.VerifySignature(request.Parameters)
            || !_vnPayClient.IsExpectedMerchant(request.Parameters))
        {
            throw Invalid("PAYMENT_SIGNATURE_INVALID", "VNPay return parameters are not authentic.");
        }

        if (!request.Parameters.TryGetValue("vnp_TxnRef", out var txnRef)
            || string.IsNullOrWhiteSpace(txnRef))
        {
            throw Invalid("PAYMENT_SESSION_INVALID", "vnp_TxnRef is required.");
        }

        var providerAmount = ParseProviderAmount(request.Parameters);
        var payment = await _payments.FindVnPayPaymentByTxnRefAsync(txnRef, cancellationToken)
            .ConfigureAwait(false);
        var topUp = payment is null
            ? await _topUpRequests.FindByVnPayTxnRefAsync(txnRef, cancellationToken).ConfigureAwait(false)
            : null;

        if (payment is null && topUp is null)
            throw Invalid("PAYMENT_SESSION_INVALID", "VNPay payment session was not found.");

        var storedAmount = payment?.Amount.Amount ?? topUp!.Amount.Amount;
        var storedMode = payment?.ReturnMode ?? topUp!.ReturnMode;
        if (storedAmount != providerAmount)
            throw Invalid("PAYMENT_AMOUNT_INVALID", "VNPay return amount does not match the payment session.");
        if (storedMode != VnPayReturnMode.MOBILE_SDK)
            throw Invalid("PAYMENT_RETURN_MODE_INVALID", "VNPay payment session is not a Mobile SDK session.");

        request.Parameters.TryGetValue("vnp_ResponseCode", out var responseCode);
        request.Parameters.TryGetValue("vnp_TransactionStatus", out var transactionStatus);

        var redirectUri = responseCode switch
        {
            "00" when transactionStatus == "00" => SuccessUri,
            "24" => CancelUri,
            _ => FailUri,
        };

        return new VnPayMobileSdkReturnResult(redirectUri);
    }

    private static long ParseProviderAmount(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("vnp_Amount", out var rawAmount)
            || !long.TryParse(rawAmount, NumberStyles.None, CultureInfo.InvariantCulture, out var scaledAmount)
            || scaledAmount <= 0
            || scaledAmount % 100 != 0)
        {
            throw Invalid("PAYMENT_AMOUNT_INVALID", "vnp_Amount is invalid.");
        }

        return scaledAmount / 100;
    }

    private static VnPayMobileReturnInvalidException Invalid(string code, string message)
        => new(code, message);
}
