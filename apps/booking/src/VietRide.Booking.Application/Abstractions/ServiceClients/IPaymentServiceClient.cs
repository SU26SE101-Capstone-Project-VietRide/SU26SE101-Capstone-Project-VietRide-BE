namespace VietRide.Booking.Application.Abstractions.ServiceClients;

// ---------------------------------------------------------------------------
// Result records for the Payment seam (API Contract line 1565)
// ---------------------------------------------------------------------------

/// <summary>
/// Result of POST /internal/v1/payments/charge.
/// Shape per VietRide_API_Contract_v1.md line 1580-1591.
/// </summary>
public sealed record ChargeResult(
    Guid PaymentId,
    string Status,
    string? PaymentRedirectUrl);

public sealed record BatchChargeItem(
    string ReferenceType,
    Guid ReferenceId,
    long Amount,
    PaymentContextSnapshot? Context = null);

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
    long VoucherOperatorFundedAmount);

public sealed record BatchChargePaymentResult(
    Guid PaymentId,
    string ReferenceType,
    Guid ReferenceId,
    string Status,
    string? PaymentRedirectUrl);

/// <summary>
/// Discriminated-union result of <see cref="IPaymentServiceClient.ChargeAsync"/>.
/// </summary>
public abstract record ChargeOutcome
{
    private ChargeOutcome() { }

    /// <summary>
    /// Payment charge succeeded (SUCCEEDED or PENDING for VNPay redirect).
    /// </summary>
    public sealed record Success(ChargeResult Data) : ChargeOutcome;

    /// <summary>
    /// Insufficient funds (WALLET path with balance below amount).
    /// </summary>
    public sealed record InsufficientFunds(string Message) : ChargeOutcome;

    /// <summary>The authoritative Payment deadline has already elapsed.</summary>
    public sealed record DeadlinePassed(string Message) : ChargeOutcome;

    /// <summary>Unexpected HTTP / transport error.</summary>
    public sealed record TransportError(string Message) : ChargeOutcome;
}

public abstract record BatchChargeOutcome
{
    private BatchChargeOutcome() { }

    public sealed record Success(IReadOnlyList<BatchChargePaymentResult> Payments) : BatchChargeOutcome;

    public sealed record InsufficientFunds(string Message) : BatchChargeOutcome;

    public sealed record TransportError(string Message) : BatchChargeOutcome;
}

// ---------------------------------------------------------------------------
// IPaymentServiceClient
// ---------------------------------------------------------------------------

/// <summary>
/// Application-facing seam for the Payment inter-service HTTP client.
/// Targets POST /internal/v1/payments/charge (API Contract line 1565).
/// <para>
/// Location: Application/Abstractions/ServiceClients/ per BSOT §3.5 line 427.
/// Impl PaymentServiceClient at Infrastructure/Http/ per BSOT §3.5 line 479.
/// </para>
/// </summary>
public interface IPaymentServiceClient
{
    /// <summary>
    /// POST /internal/v1/payments/charge — debit the wallet or initiate VNPay redirect.
    /// Idempotent: same Idempotency-Key is safe to retry.
    /// </summary>
    Task<ChargeOutcome> ChargeAsync(
        string referenceType,
        Guid referenceId,
        Guid userId,
        long amount,
        string method,
        string idempotencyKey,
        CancellationToken cancellationToken = default,
        PaymentContextSnapshot? context = null,
        DateTimeOffset? dueAt = null);

    Task<BatchChargeOutcome> BatchChargeAsync(
        Guid userId,
        string method,
        IReadOnlyList<BatchChargeItem> items,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
