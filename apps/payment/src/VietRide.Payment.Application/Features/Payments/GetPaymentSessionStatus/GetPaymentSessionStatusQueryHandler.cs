using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Payments.GetPaymentSessionStatus;

public sealed class GetPaymentSessionStatusQueryHandler
    : IRequestHandler<GetPaymentSessionStatusQuery, PaymentSessionStatusResult>
{
    private readonly IPaymentRepository _payments;
    private readonly ITopUpRequestRepository _topUpRequests;

    public GetPaymentSessionStatusQueryHandler(
        IPaymentRepository payments,
        ITopUpRequestRepository topUpRequests)
    {
        _payments = payments;
        _topUpRequests = topUpRequests;
    }

    public async Task<PaymentSessionStatusResult> Handle(
        GetPaymentSessionStatusQuery request,
        CancellationToken cancellationToken)
    {
        var payment = await _payments.GetByIdAsync(request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        if (payment is not null
            && payment.UserId == request.UserId
            && payment.Method == PaymentMethod.VNPAY
            && payment.ReturnMode == VnPayReturnMode.MOBILE_SDK)
        {
            return new PaymentSessionStatusResult(
                payment.Id,
                Normalize(payment.Status));
        }

        var topUp = await _topUpRequests.GetByIdAsync(request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        if (topUp is not null
            && topUp.UserId == request.UserId
            && topUp.ReturnMode == VnPayReturnMode.MOBILE_SDK)
        {
            return new PaymentSessionStatusResult(
                topUp.Id,
                Normalize(topUp.Status));
        }

        throw new CodedNotFoundException(
            "PAYMENT_SESSION_NOT_FOUND",
            "Payment session was not found.");
    }

    private static string Normalize(PaymentStatus status) => status switch
    {
        PaymentStatus.PENDING_REDIRECT => "PENDING",
        PaymentStatus.SUCCEEDED => "SUCCEEDED",
        PaymentStatus.FAILED => "FAILED",
        PaymentStatus.EXPIRED => "EXPIRED",
        PaymentStatus.REFUNDED => "REFUNDED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported payment status."),
    };

    private static string Normalize(TopUpRequestStatus status) => status switch
    {
        TopUpRequestStatus.PENDING => "PENDING",
        TopUpRequestStatus.SUCCEEDED => "SUCCEEDED",
        TopUpRequestStatus.FAILED => "FAILED",
        TopUpRequestStatus.EXPIRED => "EXPIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported top-up status."),
    };
}
