namespace VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

public sealed record ChargePaymentResult(
    Guid PaymentId,
    string Status,
    string? PaymentRedirectUrl,
    DateTimeOffset? DueAt = null);
