using MediatR;
using VietRide.Payment.Application.Models;

namespace VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

public sealed record ChargePaymentCommand(
    string ReferenceType,
    Guid ReferenceId,
    Guid UserId,
    long Amount,
    string Method,
    PaymentContextV1? Context,
    string? IdempotencyKey,
    string ClientIpAddress,
    DateTimeOffset? DueAt = null,
    string? PaymentReturnMode = null) : IRequest<ChargePaymentResult>;
