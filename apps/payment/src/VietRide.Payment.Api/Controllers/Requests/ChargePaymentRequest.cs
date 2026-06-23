using VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

namespace VietRide.Payment.Api.Controllers.Requests;

public sealed record ChargePaymentRequest(
    string ReferenceType,
    Guid ReferenceId,
    Guid UserId,
    long Amount,
    string Method)
{
    public ChargePaymentCommand ToCommand(string? idempotencyKey, string clientIpAddress)
        => new(
            ReferenceType,
            ReferenceId,
            UserId,
            Amount,
            Method,
            idempotencyKey,
            clientIpAddress);
}
