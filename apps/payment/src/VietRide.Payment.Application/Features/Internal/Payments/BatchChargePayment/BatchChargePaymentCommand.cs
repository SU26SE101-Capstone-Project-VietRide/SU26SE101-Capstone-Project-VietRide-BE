using MediatR;
using VietRide.Payment.Application.Models;

namespace VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;

public sealed record BatchChargePaymentCommand(
    Guid UserId,
    string Method,
    IReadOnlyList<BatchChargePaymentCommand.Item> Items,
    string? IdempotencyKey) : IRequest<BatchChargePaymentResult>
{
    public sealed record Item(
        string ReferenceType,
        Guid ReferenceId,
        long Amount,
        PaymentContextV1? Context = null);
}
