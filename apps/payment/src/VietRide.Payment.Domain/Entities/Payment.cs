using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

public sealed class Payment
{
    private Payment()
    {
    }

    private Payment(
        Guid id,
        PaymentReferenceType referenceType,
        Guid referenceId,
        Guid? userId,
        Money amount,
        PaymentMethod method,
        PaymentStatus status,
        string? idempotencyKey,
        string? paymentRedirectUrl,
        DateTimeOffset? succeededAt)
    {
        Id = id;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        UserId = userId;
        Amount = amount;
        Method = method;
        Status = status;
        IdempotencyKey = idempotencyKey;
        PaymentRedirectUrl = paymentRedirectUrl;
        SucceededAt = succeededAt;
    }

    public Guid Id { get; private set; }
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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Payment CreateSucceededWalletBookingCharge(
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

        return new Payment(
            Guid.NewGuid(),
            PaymentReferenceType.BOOKING,
            referenceId,
            userId,
            amount,
            PaymentMethod.WALLET,
            PaymentStatus.SUCCEEDED,
            idempotencyKey: null,
            paymentRedirectUrl: null,
            succeededAt: succeededAt);
    }
}
