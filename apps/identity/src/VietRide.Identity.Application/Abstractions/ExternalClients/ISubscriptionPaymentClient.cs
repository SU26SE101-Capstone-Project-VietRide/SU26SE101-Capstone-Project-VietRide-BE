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
    string PaymentMethod,
    long Amount,
    SubscriptionPaymentSnapshot Snapshot,
    string IdempotencyKey,
    string ClientIpAddress);

public sealed record SubscriptionPaymentSnapshot(
    int Version,
    Guid OperatorSubscriptionId,
    Guid PlanId,
    string PlanName,
    string BillingPeriod,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    SubscriptionBuyerSnapshot BuyerSnapshot);

public sealed record SubscriptionBuyerSnapshot(
    string Name,
    string BusinessRegistrationNumber,
    string TaxCode,
    string ContactEmail,
    string ContactPhone,
    string? AddressStreet,
    string? AddressWard,
    string? AddressDistrict,
    string? AddressProvince);

public sealed record SubscriptionPaymentCreationResult(
    Guid PaymentId,
    string Status,
    string? PaymentRedirectUrl,
    string? InvoiceStatus);
