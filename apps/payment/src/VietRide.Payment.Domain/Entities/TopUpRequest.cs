using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Domain.Entities;

/// <summary>
/// Passenger VNPay top-up request. IPN transitions this record to a terminal state.
/// </summary>
public sealed class TopUpRequest : BaseEntity<Guid>
{
    public Guid UserId { get; private set; }
    public Money Amount { get; private set; }
    public TopUpRequestStatus Status { get; private set; }
    public string VnPayTxnRef { get; private set; } = string.Empty;
    public string? VnPayResponseCode { get; private set; }
    public string? PaymentRedirectUrl { get; private set; }
    public DateTimeOffset? SucceededAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }

    private TopUpRequest() { }

    public static TopUpRequest Create(
        Guid userId,
        Money amount,
        string vnPayTxnRef,
        string? paymentRedirectUrl = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        if (amount.Amount < 10_000)
            throw new ArgumentOutOfRangeException(nameof(amount), "Top-up amount must be at least 10,000 VND.");

        if (string.IsNullOrWhiteSpace(vnPayTxnRef))
            throw new ArgumentException("VNPay transaction reference is required.", nameof(vnPayTxnRef));

        return new TopUpRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Amount = amount,
            Status = TopUpRequestStatus.PENDING,
            VnPayTxnRef = vnPayTxnRef.Trim(),
            PaymentRedirectUrl = paymentRedirectUrl,
        };
    }

    public void MarkSucceeded(string? vnPayResponseCode, DateTimeOffset succeededAt)
    {
        if (Status != TopUpRequestStatus.PENDING)
            throw new InvalidOperationException($"Cannot succeed top-up request in status {Status}.");

        Status = TopUpRequestStatus.SUCCEEDED;
        VnPayResponseCode = vnPayResponseCode;
        SucceededAt = succeededAt;
    }

    public void MarkFailed(string? vnPayResponseCode)
    {
        if (Status != TopUpRequestStatus.PENDING)
            throw new InvalidOperationException($"Cannot fail top-up request in status {Status}.");

        Status = TopUpRequestStatus.FAILED;
        VnPayResponseCode = vnPayResponseCode;
    }

    public void MarkExpired(DateTimeOffset expiredAt)
    {
        if (Status != TopUpRequestStatus.PENDING)
            throw new InvalidOperationException($"Cannot expire top-up request in status {Status}.");

        Status = TopUpRequestStatus.EXPIRED;
        ExpiredAt = expiredAt;
    }
}
