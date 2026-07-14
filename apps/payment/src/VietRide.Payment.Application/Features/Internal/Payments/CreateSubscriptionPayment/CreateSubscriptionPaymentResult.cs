namespace VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;

public sealed record CreateSubscriptionPaymentResult(
    Guid PaymentId,
    string Status,
    string? PaymentRedirectUrl,
    string? InvoiceStatus);
