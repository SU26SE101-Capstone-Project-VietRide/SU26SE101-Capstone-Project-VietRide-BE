using MediatR;

namespace VietRide.Payment.Application.Features.Payments.GetPaymentSessionStatus;

public sealed record GetPaymentSessionStatusQuery(
    Guid SessionId,
    Guid UserId) : IRequest<PaymentSessionStatusResult>;
