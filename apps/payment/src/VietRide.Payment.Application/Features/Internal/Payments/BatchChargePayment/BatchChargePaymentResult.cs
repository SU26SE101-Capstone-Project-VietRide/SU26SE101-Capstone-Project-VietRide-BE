namespace VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;

public sealed record BatchChargePaymentResult(
    IReadOnlyList<BatchChargePaymentResult.Item> Payments)
{
    public sealed record Item(
        Guid PaymentId,
        string ReferenceType,
        Guid ReferenceId,
        string Status,
        string? PaymentRedirectUrl);
}
