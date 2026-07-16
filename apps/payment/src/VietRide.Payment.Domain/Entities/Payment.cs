using System.Text.Json;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

public sealed class Payment : BaseEntity<Guid>
{
    private Payment() { }

    public PaymentReferenceType ReferenceType { get; private set; }
    public Guid ReferenceId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? OperatorId { get; private set; }
    public Money Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? VnPayTxnRef { get; private set; }
    public string? VnPayResponseCode { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? PaymentRedirectUrl { get; private set; }
    public DateTimeOffset? SucceededAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public string Context { get; private set; } = "{}";
    public bool ContextReconciliationRequired { get; private set; }

    public void AttachContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
            throw new ArgumentException("Payment context is required.", nameof(context));

        using var document = JsonDocument.Parse(context);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Payment context must be a JSON object.", nameof(context));

        if (!string.Equals(Context, "{}", StringComparison.Ordinal))
            throw new InvalidOperationException("Payment context is immutable once assigned.");

        Context = document.RootElement.GetRawText();
        ContextReconciliationRequired = false;
    }

    public void MarkContextReconciliationRequired()
    {
        if (!string.Equals(Context, "{}", StringComparison.Ordinal))
            throw new InvalidOperationException("A payment with trusted context does not require reconciliation.");

        ContextReconciliationRequired = true;
    }

    public static Payment CreatePendingRedirect(
        PaymentReferenceType referenceType,
        Guid referenceId,
        Money amount,
        PaymentMethod method,
        Guid? userId = null,
        Guid? operatorId = null,
        string? vnPayTxnRef = null,
        string? idempotencyKey = null,
        string? paymentRedirectUrl = null)
    {
        if (referenceId == Guid.Empty)
            throw new ArgumentException("Reference id cannot be empty.", nameof(referenceId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id cannot be empty.", nameof(operatorId));

        return new Payment
        {
            Id = Guid.NewGuid(),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            UserId = userId,
            OperatorId = operatorId,
            Amount = amount,
            Method = method,
            Status = PaymentStatus.PENDING_REDIRECT,
            VnPayTxnRef = vnPayTxnRef,
            IdempotencyKey = idempotencyKey,
            PaymentRedirectUrl = paymentRedirectUrl,
        };
    }

    public static Payment CreateSucceededWalletCharge(
        PaymentReferenceType referenceType,
        Guid referenceId,
        Guid userId,
        Money amount,
        DateTimeOffset succeededAt)
    {
        if (referenceId == Guid.Empty)
            throw new ArgumentException("Reference id is required.", nameof(referenceId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");

        return new Payment
        {
            Id = Guid.NewGuid(),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            UserId = userId,
            Amount = amount,
            Method = PaymentMethod.WALLET,
            Status = PaymentStatus.SUCCEEDED,
            SucceededAt = succeededAt,
        };
    }

    public static Payment CreateSucceededWalletBookingCharge(
        Guid referenceId,
        Guid userId,
        Money amount,
        DateTimeOffset succeededAt)
        => CreateSucceededWalletCharge(PaymentReferenceType.BOOKING, referenceId, userId, amount, succeededAt);

    public static Payment CreatePendingRedirectVnPay(
        PaymentReferenceType referenceType,
        Guid referenceId,
        Guid userId,
        Money amount,
        string vnPayTxnRef,
        string idempotencyKey,
        string paymentRedirectUrl)
    {
        if (referenceId == Guid.Empty)
            throw new ArgumentException("Reference id is required.", nameof(referenceId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
        if (string.IsNullOrWhiteSpace(vnPayTxnRef))
            throw new ArgumentException("VNPay transaction reference is required.", nameof(vnPayTxnRef));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(paymentRedirectUrl))
            throw new ArgumentException("Payment redirect URL is required.", nameof(paymentRedirectUrl));

        return new Payment
        {
            Id = Guid.NewGuid(),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            UserId = userId,
            Amount = amount,
            Method = PaymentMethod.VNPAY,
            Status = PaymentStatus.PENDING_REDIRECT,
            VnPayTxnRef = vnPayTxnRef,
            IdempotencyKey = idempotencyKey,
            PaymentRedirectUrl = paymentRedirectUrl,
        };
    }

    public static Payment CreatePendingRedirectVnPayBooking(
        Guid referenceId,
        Guid userId,
        Money amount,
        string vnPayTxnRef,
        string idempotencyKey,
        string paymentRedirectUrl)
        => CreatePendingRedirectVnPay(PaymentReferenceType.BOOKING, referenceId, userId, amount,
            vnPayTxnRef, idempotencyKey, paymentRedirectUrl);

    public static Payment CreatePendingRedirectVnPaySubscription(
        Guid upgradeAttemptId,
        Guid operatorId,
        Money amount,
        string vnPayTxnRef,
        string idempotencyKey,
        string paymentRedirectUrl)
    {
        if (upgradeAttemptId == Guid.Empty)
            throw new ArgumentException("Upgrade attempt id is required.", nameof(upgradeAttemptId));
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(operatorId));
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
        if (string.IsNullOrWhiteSpace(vnPayTxnRef))
            throw new ArgumentException("VNPay transaction reference is required.", nameof(vnPayTxnRef));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(paymentRedirectUrl))
            throw new ArgumentException("Payment redirect URL is required.", nameof(paymentRedirectUrl));

        return new Payment
        {
            Id = Guid.NewGuid(),
            ReferenceType = PaymentReferenceType.SUBSCRIPTION,
            ReferenceId = upgradeAttemptId,
            OperatorId = operatorId,
            Amount = amount,
            Method = PaymentMethod.VNPAY,
            Status = PaymentStatus.PENDING_REDIRECT,
            VnPayTxnRef = vnPayTxnRef,
            IdempotencyKey = idempotencyKey,
            PaymentRedirectUrl = paymentRedirectUrl,
        };
    }

    public void MarkSucceeded(string? vnPayResponseCode, DateTimeOffset succeededAt)
    {
        Status = PaymentStatus.SUCCEEDED;
        VnPayResponseCode = vnPayResponseCode;
        SucceededAt = succeededAt;
    }

    public void MarkFailed(string? vnPayResponseCode, DateTimeOffset failedAt)
    {
        Status = PaymentStatus.FAILED;
        VnPayResponseCode = vnPayResponseCode;
        FailedAt = failedAt;
    }

    public void MarkExpired(DateTimeOffset expiredAt)
    {
        Status = PaymentStatus.EXPIRED;
        ExpiredAt = expiredAt;
    }

    public void MarkRefunded(DateTimeOffset refundedAt)
    {
        Status = PaymentStatus.REFUNDED;
        RefundedAt = refundedAt;
    }
}
