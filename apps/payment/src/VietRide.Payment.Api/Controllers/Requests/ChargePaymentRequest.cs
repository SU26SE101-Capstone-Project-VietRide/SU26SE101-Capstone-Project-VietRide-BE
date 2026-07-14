using VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;
using VietRide.Payment.Application.Models;

namespace VietRide.Payment.Api.Controllers.Requests;

public sealed record ChargePaymentRequest(
    string ReferenceType,
    Guid ReferenceId,
    Guid UserId,
    long Amount,
    string Method,
    PaymentContextV1? Context)
{
    public ChargePaymentCommand ToCommand(string? idempotencyKey, string clientIpAddress)
        => new(
            ReferenceType,
            ReferenceId,
            UserId,
            Amount,
            Method,
            Context,
            idempotencyKey,
            clientIpAddress);
}
