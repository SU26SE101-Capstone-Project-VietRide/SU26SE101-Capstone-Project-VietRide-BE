namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IPaymentServiceClient
{
    Task<ChargeOutcome> ChargeParcelPaymentAsync(
        string referenceType,
        Guid referenceId,
        Guid userId,
        long amount,
        string method,
        string idempotencyKey,
        CancellationToken cancellationToken = default,
        PaymentContextSnapshot? context = null,
        DateTimeOffset? dueAt = null,
        string? paymentReturnMode = null);

    Task<RefundOutcome> RefundParcelPaymentAsync(
        Guid userId,
        long amount,
        string referenceType,
        Guid referenceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed record PaymentContextSnapshot(
    int Version,
    IReadOnlyList<PaymentAllocationSnapshot> Allocations);

public sealed record PaymentAllocationSnapshot(
    Guid ReferenceId,
    string ReferenceType,
    Guid OperatorId,
    Guid TripId,
    long GrossAmount,
    long VoucherVietRideFundedAmount,
    long VoucherOperatorFundedAmount,
    string? ReferenceCode = null);

public sealed record VnPaySdkMetadata(
    string TmnCode,
    string Scheme,
    bool IsSandbox);
