using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

public sealed record ChargePaymentCommand(
    string ReferenceType,
    Guid ReferenceId,
    Guid UserId,
    long Amount,
    string Method,
    string? IdempotencyKey,
    string ClientIpAddress) : IRequest<ChargePaymentResult>;
