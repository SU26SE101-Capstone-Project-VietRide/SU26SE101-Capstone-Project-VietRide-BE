using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelCustodyExceptionRequest : BaseEntity<Guid>
{
    public Guid ParcelId { get; private set; }
    public Guid IncidentId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid TripId { get; private set; }
    public ParcelIncidentType IncidentType { get; private set; }
    public ParcelCustodyLocationType ActualLocationType { get; private set; }
    public Guid? ActualLocationId { get; private set; }
    public string? LocationSnapshot { get; private set; }
    public string? TemporaryExceptionTag { get; private set; }
    public string? Description { get; private set; }
    public decimal? ObservedWeightKg { get; private set; }
    public string EvidenceReferencesJson { get; private set; } = "[]";
    public string Reason { get; private set; } = string.Empty;
    public ParcelCustodyExceptionRequestStatus Status { get; private set; }
    public Guid ReportedByUserId { get; private set; }
    public string ReportedByRole { get; private set; } = "ASSISTANT";
    public DateTimeOffset ReportedAt { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ReviewedByRole { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewNote { get; private set; }
    public Guid? ApprovedCustodyEventId { get; private set; }
    public Guid IdempotencyKey { get; private set; }

    private ParcelCustodyExceptionRequest()
    {
    }

    public static ParcelCustodyExceptionRequest Create(
        Guid parcelId,
        Guid incidentId,
        Guid operatorId,
        Guid tripId,
        ParcelIncidentType incidentType,
        ParcelCustodyLocationType actualLocationType,
        Guid? actualLocationId,
        string? locationSnapshot,
        string? temporaryExceptionTag,
        string? description,
        decimal? observedWeightKg,
        string evidenceReferencesJson,
        string reason,
        Guid reportedByUserId,
        string reportedByRole,
        DateTimeOffset reportedAt,
        Guid idempotencyKey)
    {
        if (parcelId == Guid.Empty || incidentId == Guid.Empty || operatorId == Guid.Empty
            || tripId == Guid.Empty || reportedByUserId == Guid.Empty || idempotencyKey == Guid.Empty)
            throw new ArgumentException("Required custody exception identifiers cannot be empty.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));

        return new ParcelCustodyExceptionRequest
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            IncidentId = incidentId,
            OperatorId = operatorId,
            TripId = tripId,
            IncidentType = incidentType,
            ActualLocationType = actualLocationType,
            ActualLocationId = actualLocationId,
            LocationSnapshot = Normalize(locationSnapshot),
            TemporaryExceptionTag = Normalize(temporaryExceptionTag),
            Description = Normalize(description),
            ObservedWeightKg = observedWeightKg,
            EvidenceReferencesJson = string.IsNullOrWhiteSpace(evidenceReferencesJson)
                ? "[]"
                : evidenceReferencesJson,
            Reason = reason.Trim(),
            Status = ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL,
            ReportedByUserId = reportedByUserId,
            ReportedByRole = Normalize(reportedByRole) ?? "ASSISTANT",
            ReportedAt = reportedAt,
            IdempotencyKey = idempotencyKey,
        };
    }

    public void Approve(
        Guid reviewedByUserId,
        string reviewedByRole,
        string? reviewNote,
        Guid approvedCustodyEventId,
        DateTimeOffset reviewedAt)
    {
        EnsurePending();
        if (reviewedByUserId == Guid.Empty || approvedCustodyEventId == Guid.Empty)
            throw new ArgumentException("Reviewer and custody event ids are required.");
        Status = ParcelCustodyExceptionRequestStatus.APPROVED;
        SetReviewAudit(reviewedByUserId, reviewedByRole, reviewNote, reviewedAt);
        ApprovedCustodyEventId = approvedCustodyEventId;
        RowVersion++;
    }

    public void Reject(
        Guid reviewedByUserId,
        string reviewedByRole,
        string? reviewNote,
        DateTimeOffset reviewedAt)
    {
        EnsurePending();
        if (reviewedByUserId == Guid.Empty)
            throw new ArgumentException("Reviewer id is required.", nameof(reviewedByUserId));
        Status = ParcelCustodyExceptionRequestStatus.REJECTED;
        SetReviewAudit(reviewedByUserId, reviewedByRole, reviewNote, reviewedAt);
        RowVersion++;
    }

    private void EnsurePending()
    {
        if (Status != ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL)
            throw new InvalidOperationException("Custody exception request has already been decided.");
    }

    private void SetReviewAudit(
        Guid reviewedByUserId,
        string reviewedByRole,
        string? reviewNote,
        DateTimeOffset reviewedAt)
    {
        ReviewedByUserId = reviewedByUserId;
        ReviewedByRole = Normalize(reviewedByRole)
            ?? throw new ArgumentException("Reviewer role is required.", nameof(reviewedByRole));
        ReviewedAt = reviewedAt;
        ReviewNote = Normalize(reviewNote);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
