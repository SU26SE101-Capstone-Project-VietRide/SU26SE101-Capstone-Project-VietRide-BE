namespace VietRide.Identity.Application.Abstractions.ExternalClients;

public interface ISubscriptionPaymentClient
{
    Task<SubscriptionPaymentCreationResult> CreateAsync(
        SubscriptionPaymentCreationRequest request,
        CancellationToken cancellationToken = default);

    Task ExpireAsync(Guid paymentId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionPaymentStatusResult>> GetStatusesAsync(
        IReadOnlyCollection<Guid> upgradeAttemptIds,
        CancellationToken cancellationToken = default);
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
    string ReturnMode,
    string IdempotencyKey,
    string ClientIpAddress,
    DateTimeOffset? DueAt = null);

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

public sealed record SubscriptionPaymentStatusResult(
    Guid PaymentId,
    Guid UpgradeAttemptId,
    Guid OperatorId,
    Guid OperatorSubscriptionId,
    Guid PlanId,
    string Status,
    long Amount,
    string Method,
    string BillingPeriod,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    DateTimeOffset? SucceededAt,
    DateTimeOffset? DueAt);
