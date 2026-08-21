using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

public sealed class SubscriptionCustomRequest : BaseEntity<Guid>
{
    public Guid OperatorId { get; private set; }
    public int MaxVehicles { get; private set; }
    public int MaxDrivers { get; private set; }
    public int MaxAssistants { get; private set; }
    public int MaxOperatorUsers { get; private set; }
    public int MaxRoutes { get; private set; }
    public int MaxTripsPerMonth { get; private set; }
    public bool EnableParcel { get; private set; }
    public bool EnableShuttle { get; private set; }
    public bool EnableRag { get; private set; }
    public SubscriptionBillingPeriod PreferredBillingPeriod { get; private set; }
    public string? Note { get; private set; }
    public SubscriptionCustomRequestStatus Status { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? ApprovedPlanId { get; private set; }

    private SubscriptionCustomRequest() { }

    public static SubscriptionCustomRequest Create(
        Guid operatorId,
        int maxVehicles,
        int maxDrivers,
        int maxAssistants,
        int maxOperatorUsers,
        int maxRoutes,
        int maxTripsPerMonth,
        bool enableParcel,
        bool enableShuttle,
        bool enableRag,
        SubscriptionBillingPeriod preferredBillingPeriod,
        string? note)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(operatorId));
        EnsureNonNegative(maxVehicles, nameof(maxVehicles));
        EnsureNonNegative(maxDrivers, nameof(maxDrivers));
        EnsureNonNegative(maxAssistants, nameof(maxAssistants));
        EnsureNonNegative(maxOperatorUsers, nameof(maxOperatorUsers));
        EnsureNonNegative(maxRoutes, nameof(maxRoutes));
        EnsureNonNegative(maxTripsPerMonth, nameof(maxTripsPerMonth));

        return new SubscriptionCustomRequest
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            MaxVehicles = maxVehicles,
            MaxDrivers = maxDrivers,
            MaxAssistants = maxAssistants,
            MaxOperatorUsers = maxOperatorUsers,
            MaxRoutes = maxRoutes,
            MaxTripsPerMonth = maxTripsPerMonth,
            EnableParcel = enableParcel,
            EnableShuttle = enableShuttle,
            EnableRag = enableRag,
            PreferredBillingPeriod = preferredBillingPeriod,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = SubscriptionCustomRequestStatus.PENDING_REVIEW,
        };
    }

    public void Approve(Guid reviewerId, Guid approvedPlanId, DateTimeOffset reviewedAt)
    {
        EnsurePending();
        if (reviewerId == Guid.Empty || approvedPlanId == Guid.Empty)
            throw new ArgumentException("Reviewer and approved plan ids are required.");

        Status = SubscriptionCustomRequestStatus.APPROVED;
        ReviewedBy = reviewerId;
        ReviewedAt = reviewedAt;
        ApprovedPlanId = approvedPlanId;
        RejectionReason = null;
    }

    public void Reject(Guid reviewerId, string reason, DateTimeOffset reviewedAt)
    {
        EnsurePending();
        if (reviewerId == Guid.Empty)
            throw new ArgumentException("Reviewer id is required.", nameof(reviewerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = SubscriptionCustomRequestStatus.REJECTED;
        ReviewedBy = reviewerId;
        ReviewedAt = reviewedAt;
        RejectionReason = reason.Trim();
        ApprovedPlanId = null;
    }

    private void EnsurePending()
    {
        if (Status != SubscriptionCustomRequestStatus.PENDING_REVIEW)
            throw new InvalidOperationException("Only a pending custom request can be reviewed.");
    }

    private static void EnsureNonNegative(int value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
    }
}
