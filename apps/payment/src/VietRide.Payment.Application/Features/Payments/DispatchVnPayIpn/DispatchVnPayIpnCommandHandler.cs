using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Payments.ConfirmBookingPayment;
using VietRide.Payment.Application.Features.Payments.ConfirmSubscriptionPayment;
using VietRide.Payment.Application.Features.TopUps.ConfirmTopUp;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Features.Payments.DispatchVnPayIpn;

public sealed class DispatchVnPayIpnCommandHandler
    : IRequestHandler<DispatchVnPayIpnCommand, DispatchVnPayIpnResult>
{
    private readonly IVnPayClient _vnPayClient;
    private readonly IPaymentRepository _payments;
    private readonly IMediator _mediator;
    private readonly ILogger<DispatchVnPayIpnCommandHandler> _logger;

    public DispatchVnPayIpnCommandHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments,
        IMediator mediator,
        ILogger<DispatchVnPayIpnCommandHandler> logger)
    {
        _vnPayClient = vnPayClient;
        _payments = payments;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<DispatchVnPayIpnResult> Handle(
        DispatchVnPayIpnCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Parameters.Count == 0)
        {
            _logger.LogInformation("Rejected empty VNPay IPN connectivity probe.");
            return new DispatchVnPayIpnResult("99", "INPUT_DATA_REQUIRED");
        }

        if (!_vnPayClient.VerifySignature(request.Parameters))
        {
            request.Parameters.TryGetValue("vnp_TxnRef", out var invalidTxnRef);
            request.Parameters.TryGetValue("vnp_TmnCode", out var invalidMerchant);
            _logger.LogWarning(
                "Rejected VNPay IPN with invalid signature for transaction {VnPayTxnRef} and merchant {TmnCode}.",
                invalidTxnRef,
                invalidMerchant);
            return new DispatchVnPayIpnResult("97", "PAYMENT_SIGNATURE_INVALID");
        }
        if (!_vnPayClient.IsExpectedMerchant(request.Parameters))
        {
            request.Parameters.TryGetValue("vnp_TmnCode", out var unexpectedMerchant);
            _logger.LogWarning("Rejected VNPay IPN for unexpected merchant {TmnCode}.", unexpectedMerchant);
            return new DispatchVnPayIpnResult("99", "INVALID_MERCHANT");
        }
        if (!request.Parameters.TryGetValue("vnp_TxnRef", out var txnRef) || string.IsNullOrWhiteSpace(txnRef))
            return new DispatchVnPayIpnResult("01", "Order Not Found");

        var payment = _payments.QueryNoTracking().FirstOrDefault(candidate =>
            candidate.Method == PaymentMethod.VNPAY && candidate.VnPayTxnRef == txnRef);
        if (payment?.ReferenceType == PaymentReferenceType.SUBSCRIPTION)
        {
            _logger.LogInformation("Dispatching VNPay IPN {VnPayTxnRef} to subscription payment confirmation.", txnRef);
            var result = await _mediator.Send(
                new ConfirmSubscriptionPaymentCommand(request.Parameters),
                cancellationToken).ConfigureAwait(false);
            return new DispatchVnPayIpnResult(result.RspCode, result.Message);
        }

        if (payment is not null)
        {
            _logger.LogInformation(
                "Dispatching VNPay IPN {VnPayTxnRef} to {ReferenceType} payment confirmation.",
                txnRef,
                payment.ReferenceType);
            var result = await _mediator.Send(
                new ConfirmBookingPaymentCommand(request.Parameters),
                cancellationToken).ConfigureAwait(false);
            return new DispatchVnPayIpnResult(result.RspCode, result.Message);
        }

        _logger.LogInformation("Dispatching VNPay IPN {VnPayTxnRef} to wallet top-up confirmation.", txnRef);
        var topUpResult = await _mediator.Send(
            new ConfirmTopUpCommand(request.Parameters),
            cancellationToken).ConfigureAwait(false);
        return new DispatchVnPayIpnResult(topUpResult.RspCode, topUpResult.Message);
    }
}
