namespace VietRide.Payment.Application.Features.TopUps.CreateTopUp;

public sealed record CreateTopUpResult(
    Guid TopUpRequestId,
    string Status,
    string PaymentRedirectUrl);
