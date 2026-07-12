namespace VietRide.Identity.Application.Abstractions.ExternalClients;

public interface ISubscriptionPaymentClient
{
    Task<SubscriptionPaymentCreationResult> CreateAsync(
        SubscriptionPaymentCreationRequest request,
        CancellationToken cancellationToken = default);

    Task ExpireAsync(Guid paymentId, string idempotencyKey, CancellationToken cancellationToken = default);
}

public sealed record SubscriptionPaymentCreationRequest(
    Guid UpgradeAttemptId,
    Guid SubscriptionId,
    Guid OperatorId,
    Guid PlanId,
    string BillingPeriod,
    long Amount,
    string IdempotencyKey,
    string ClientIpAddress);

public sealed record SubscriptionPaymentCreationResult(
    Guid PaymentId,
    string Status,
    string PaymentRedirectUrl);
