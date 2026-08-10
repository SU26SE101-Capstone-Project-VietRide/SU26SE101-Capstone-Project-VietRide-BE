namespace VietRide.Payment.Application.Features.Payments.GetPaymentSessionStatus;

public sealed record PaymentSessionStatusResult(
    Guid SessionId,
    string Status);
