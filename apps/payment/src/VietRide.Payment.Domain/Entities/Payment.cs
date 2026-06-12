using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

public sealed class Payment : BaseEntity<Guid>
{
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

    private Payment() { }

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
