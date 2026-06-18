using VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;

namespace VietRide.Payment.Api.Controllers.Requests;

public sealed record BatchChargePaymentRequest(
    Guid UserId,
    string Method,
    IReadOnlyList<BatchChargePaymentRequest.Item> Items)
{
    public BatchChargePaymentCommand ToCommand(string? idempotencyKey)
        => new(
            UserId,
            Method,
            Items.Select(x => new BatchChargePaymentCommand.Item(x.ReferenceType, x.ReferenceId, x.Amount)).ToList(),
            idempotencyKey);

    public sealed record Item(
        string ReferenceType,
        Guid ReferenceId,
        long Amount);
}
