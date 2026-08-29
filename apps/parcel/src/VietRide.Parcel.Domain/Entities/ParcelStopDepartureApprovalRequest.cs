using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelStopDepartureApprovalRequest : BaseEntity<Guid>
{
    public Guid TripId { get; private set; }
    public Guid StopId { get; private set; }
    public Guid OperatorId { get; private set; }
    public string UnresolvedParcelIdsJson { get; private set; } = "[]";
    public string DepartureOverrideReason { get; private set; } = string.Empty;
    public ParcelStopDepartureApprovalStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string RequestedByRole { get; private set; } = "ASSISTANT";
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ReviewedByRole { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewNote { get; private set; }
    public Guid IdempotencyKey { get; private set; }

    private ParcelStopDepartureApprovalRequest()
    {
    }

    public static ParcelStopDepartureApprovalRequest Create(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        string unresolvedParcelIdsJson,
        string departureOverrideReason,
        Guid requestedByUserId,
        string requestedByRole,
        DateTimeOffset requestedAt,
        Guid idempotencyKey)
    {
        if (tripId == Guid.Empty || stopId == Guid.Empty || operatorId == Guid.Empty
            || requestedByUserId == Guid.Empty || idempotencyKey == Guid.Empty)
            throw new ArgumentException("Required departure approval identifiers cannot be empty.");
        if (string.IsNullOrWhiteSpace(unresolvedParcelIdsJson) || unresolvedParcelIdsJson == "[]")
            throw new ArgumentException("At least one unresolved Parcel is required.", nameof(unresolvedParcelIdsJson));
        if (string.IsNullOrWhiteSpace(departureOverrideReason))
            throw new ArgumentException("Departure override reason is required.", nameof(departureOverrideReason));

        return new ParcelStopDepartureApprovalRequest
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            StopId = stopId,
            OperatorId = operatorId,
            UnresolvedParcelIdsJson = unresolvedParcelIdsJson,
            DepartureOverrideReason = departureOverrideReason.Trim(),
            Status = ParcelStopDepartureApprovalStatus.PENDING_APPROVAL,
            RequestedByUserId = requestedByUserId,
            RequestedByRole = Normalize(requestedByRole) ?? "ASSISTANT",
            RequestedAt = requestedAt,
            IdempotencyKey = idempotencyKey,
        };
    }

    public void Approve(
        Guid reviewedByUserId,
        string reviewedByRole,
        string? reviewNote,
        DateTimeOffset reviewedAt)
    {
        EnsurePending();
        SetReviewAudit(reviewedByUserId, reviewedByRole, reviewNote, reviewedAt);
        Status = ParcelStopDepartureApprovalStatus.APPROVED;
        RowVersion++;
    }

    public void Reject(
        Guid reviewedByUserId,
        string reviewedByRole,
        string? reviewNote,
        DateTimeOffset reviewedAt)
    {
        EnsurePending();
        SetReviewAudit(reviewedByUserId, reviewedByRole, reviewNote, reviewedAt);
        Status = ParcelStopDepartureApprovalStatus.REJECTED;
        RowVersion++;
    }

    public void CancelAsSuperseded(DateTimeOffset cancelledAt)
    {
        EnsurePending();
        Status = ParcelStopDepartureApprovalStatus.CANCELLED;
        ReviewedByUserId = null;
        ReviewedByRole = "SYSTEM";
        ReviewedAt = cancelledAt;
        ReviewNote = "Superseded by a newer reconciliation snapshot.";
        RowVersion++;
    }

    private void EnsurePending()
    {
        if (Status != ParcelStopDepartureApprovalStatus.PENDING_APPROVAL)
            throw new InvalidOperationException("Stop departure approval request has already been decided.");
    }

    private void SetReviewAudit(
        Guid reviewedByUserId,
        string reviewedByRole,
        string? reviewNote,
        DateTimeOffset reviewedAt)
    {
        if (reviewedByUserId == Guid.Empty)
            throw new ArgumentException("Reviewer id is required.", nameof(reviewedByUserId));
        ReviewedByUserId = reviewedByUserId;
        ReviewedByRole = Normalize(reviewedByRole)
            ?? throw new ArgumentException("Reviewer role is required.", nameof(reviewedByRole));
        ReviewedAt = reviewedAt;
        ReviewNote = Normalize(reviewNote);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
