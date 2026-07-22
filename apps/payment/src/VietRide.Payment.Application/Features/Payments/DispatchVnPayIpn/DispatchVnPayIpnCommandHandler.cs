using MediatR;
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

    public DispatchVnPayIpnCommandHandler(
        IVnPayClient vnPayClient,
        IPaymentRepository payments,
        IMediator mediator)
    {
        _vnPayClient = vnPayClient;
        _payments = payments;
        _mediator = mediator;
    }

    public async Task<DispatchVnPayIpnResult> Handle(
        DispatchVnPayIpnCommand request,
        CancellationToken cancellationToken)
    {
        if (!_vnPayClient.VerifySignature(request.Parameters))
            return new DispatchVnPayIpnResult("97", "PAYMENT_SIGNATURE_INVALID");
        if (!_vnPayClient.IsExpectedMerchant(request.Parameters))
            return new DispatchVnPayIpnResult("99", "INVALID_MERCHANT");
        if (!request.Parameters.TryGetValue("vnp_TxnRef", out var txnRef) || string.IsNullOrWhiteSpace(txnRef))
            return new DispatchVnPayIpnResult("01", "Order Not Found");

        var payment = _payments.QueryNoTracking().FirstOrDefault(candidate =>
            candidate.Method == PaymentMethod.VNPAY && candidate.VnPayTxnRef == txnRef);
        if (payment?.ReferenceType == PaymentReferenceType.SUBSCRIPTION)
        {
            var result = await _mediator.Send(
                new ConfirmSubscriptionPaymentCommand(request.Parameters),
                cancellationToken).ConfigureAwait(false);
            return new DispatchVnPayIpnResult(result.RspCode, result.Message);
        }

        if (payment is not null)
        {
            var result = await _mediator.Send(
                new ConfirmBookingPaymentCommand(request.Parameters),
                cancellationToken).ConfigureAwait(false);
            return new DispatchVnPayIpnResult(result.RspCode, result.Message);
        }

        var topUpResult = await _mediator.Send(
            new ConfirmTopUpCommand(request.Parameters),
            cancellationToken).ConfigureAwait(false);
        return new DispatchVnPayIpnResult(topUpResult.RspCode, topUpResult.Message);
    }
}
