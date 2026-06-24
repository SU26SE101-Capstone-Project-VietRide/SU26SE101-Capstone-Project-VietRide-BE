using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Domain.Entities;

public sealed class RefundFailureLog : BaseEntity<Guid>
{
    private const int MaxRetryCount = 5;

    private RefundFailureLog() { }

    public Guid? BookingId { get; private set; }
    public Guid? ParcelId { get; private set; }
    public string TriggerEventType { get; private set; } = string.Empty;
    public string FailureReason { get; private set; } = string.Empty;
    public int RetryCount { get; private set; }
    public DateTimeOffset LastAttemptAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }

    public bool IsResolved => ResolvedAt.HasValue;
    public bool CanRetry => !IsResolved && RetryCount < MaxRetryCount;
    public bool IsRetryExhausted => !IsResolved && RetryCount >= MaxRetryCount;

    public static RefundFailureLog CreateForBooking(
        Guid bookingId,
        string triggerEventType,
        string failureReason,
        DateTimeOffset occurredAt)
    {
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking id is required.", nameof(bookingId));

        return Create(bookingId, parcelId: null, triggerEventType, failureReason, occurredAt);
    }

    public static RefundFailureLog CreateForParcel(
        Guid parcelId,
        string triggerEventType,
        string failureReason,
        DateTimeOffset occurredAt)
    {
        if (parcelId == Guid.Empty)
            throw new ArgumentException("Parcel id is required.", nameof(parcelId));

        return Create(bookingId: null, parcelId, triggerEventType, failureReason, occurredAt);
    }

    public void RecordRetryAttempt(DateTimeOffset attemptedAt)
    {
        if (IsResolved)
            throw new InvalidOperationException("Resolved refund failure logs cannot be retried.");
        if (RetryCount >= MaxRetryCount)
            throw new InvalidOperationException("Refund retry count has been exhausted.");

        RetryCount++;
        LastAttemptAt = attemptedAt;
    }

    public void RecordRetryFailure(DateTimeOffset attemptedAt, string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));

        RecordRetryAttempt(attemptedAt);
        FailureReason = failureReason;
    }

    public void RecordRetryExhausted(string failureReason)
    {
        if (!IsRetryExhausted)
            throw new InvalidOperationException("Refund retry count has not been exhausted.");
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));

        FailureReason = failureReason;
    }

    public void Resolve(DateTimeOffset resolvedAt, Guid? resolvedByUserId = null)
    {
        if (resolvedByUserId == Guid.Empty)
            throw new ArgumentException("Resolved by user id cannot be empty.", nameof(resolvedByUserId));

        ResolvedAt = resolvedAt;
        ResolvedByUserId = resolvedByUserId;
    }

    private static RefundFailureLog Create(
        Guid? bookingId,
        Guid? parcelId,
        string triggerEventType,
        string failureReason,
        DateTimeOffset occurredAt)
    {
        if (bookingId is null && parcelId is null)
            throw new ArgumentException("A booking or parcel target is required.");
        if (string.IsNullOrWhiteSpace(triggerEventType))
            throw new ArgumentException("Trigger event type is required.", nameof(triggerEventType));
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));

        return new RefundFailureLog
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            ParcelId = parcelId,
            TriggerEventType = triggerEventType,
            FailureReason = failureReason,
            LastAttemptAt = occurredAt,
        };
    }
}
