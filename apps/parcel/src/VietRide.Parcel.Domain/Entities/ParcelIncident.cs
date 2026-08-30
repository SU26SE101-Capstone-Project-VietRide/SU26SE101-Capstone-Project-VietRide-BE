using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelIncident : BaseEntity<Guid>
{
    public Guid ParcelId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid? TripId { get; private set; }
    public Guid? LegId { get; private set; }
    public ParcelIncidentType Type { get; private set; }
    public ParcelIncidentStatus Status { get; private set; }
    public string? ExpectedLocation { get; private set; }
    public string? LastKnownLocation { get; private set; }
    public Guid? ReporterId { get; private set; }
    public string ReporterSource { get; private set; } = "SYSTEM";
    public string? Description { get; private set; }
    public string? EvidenceJson { get; private set; }
    public DateTimeOffset? SearchDeadline { get; private set; }
    public DateTimeOffset? EscalatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolutionCode { get; private set; }
    public string? ResolutionNote { get; private set; }
    public bool OperatorProcessBreach { get; private set; }

    private ParcelIncident()
    {
    }

    public static ParcelIncident Open(
        Guid parcelId,
        Guid operatorId,
        ParcelIncidentType type,
        DateTimeOffset? searchDeadline,
        Guid? tripId,
        Guid? legId,
        Guid? reporterId,
        string reporterSource,
        string? expectedLocation,
        string? lastKnownLocation,
        string? description,
        string? evidenceJson,
        bool operatorProcessBreach)
    {
        if (parcelId == Guid.Empty || operatorId == Guid.Empty)
            throw new ArgumentException("Parcel and operator ids are required.");

        return new ParcelIncident
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            OperatorId = operatorId,
            TripId = tripId,
            LegId = legId,
            Type = type,
            Status = ParcelIncidentStatus.OPEN,
            SearchDeadline = searchDeadline,
            ReporterId = reporterId,
            ReporterSource = Normalize(reporterSource) ?? "SYSTEM",
            ExpectedLocation = Normalize(expectedLocation),
            LastKnownLocation = Normalize(lastKnownLocation),
            Description = Normalize(description),
            EvidenceJson = Normalize(evidenceJson),
            OperatorProcessBreach = operatorProcessBreach,
        };
    }

    public void StartSearch(DateTimeOffset? searchDeadline = null)
    {
        if (Status != ParcelIncidentStatus.OPEN)
            throw new InvalidOperationException("Only open incidents can enter search.");
        if (searchDeadline.HasValue)
            SearchDeadline = searchDeadline.Value;
        if (!SearchDeadline.HasValue)
            throw new InvalidOperationException("A search deadline is required before search can start.");
        Status = ParcelIncidentStatus.SEARCHING;
    }

    public void MarkFound(string? note)
    {
        if (Status is not (ParcelIncidentStatus.OPEN
            or ParcelIncidentStatus.SEARCHING
            or ParcelIncidentStatus.ESCALATED
            or ParcelIncidentStatus.SEARCH_EXPIRED))
            throw new InvalidOperationException("Only an active search incident can be marked found.");
        Status = ParcelIncidentStatus.FOUND;
        ResolutionNote = Normalize(note);
    }

    public void StartForwarding()
    {
        if (Status != ParcelIncidentStatus.FOUND)
            throw new InvalidOperationException("Only found incidents can be forwarded.");
        Status = ParcelIncidentStatus.FORWARDING;
    }

    public void Resolve(string resolutionCode, string? note, DateTimeOffset resolvedAt)
    {
        if (Status is not (ParcelIncidentStatus.FOUND or ParcelIncidentStatus.FORWARDING))
            throw new InvalidOperationException("Only found or forwarding incidents can be resolved.");
        Status = ParcelIncidentStatus.RESOLVED;
        ResolutionCode = Normalize(resolutionCode) ?? throw new ArgumentException("Resolution code is required.");
        ResolutionNote = Normalize(note);
        ResolvedAt = resolvedAt;
    }

    public void RejectReport(string? note, DateTimeOffset resolvedAt)
    {
        if (Status is not (ParcelIncidentStatus.OPEN or ParcelIncidentStatus.SEARCHING))
            throw new InvalidOperationException("Only an open or searching report can be rejected.");
        Status = ParcelIncidentStatus.RESOLVED;
        ResolutionCode = "SUPERVISOR_REJECTED";
        ResolutionNote = Normalize(note);
        ResolvedAt = resolvedAt;
    }

    public void MarkOperatorProcessBreach()
    {
        if (Status is ParcelIncidentStatus.CLOSED or ParcelIncidentStatus.RESOLVED)
            throw new InvalidOperationException("A closed incident cannot be marked as a process breach.");
        OperatorProcessBreach = true;
    }

    public void Escalate(DateTimeOffset at)
    {
        if (Status is not (ParcelIncidentStatus.OPEN or ParcelIncidentStatus.SEARCHING))
            throw new InvalidOperationException("Only an open or searching incident can be escalated.");
        Status = ParcelIncidentStatus.ESCALATED;
        EscalatedAt = at;
    }

    public void ExpireSearch()
    {
        if (Status != ParcelIncidentStatus.ESCALATED)
            throw new InvalidOperationException("Only an escalated incident can expire its search.");
        Status = ParcelIncidentStatus.SEARCH_EXPIRED;
    }

    public void ConfirmLost(string? note, DateTimeOffset at)
    {
        if (Status != ParcelIncidentStatus.SEARCH_EXPIRED)
            throw new InvalidOperationException("Only an expired search can be confirmed lost.");
        Status = ParcelIncidentStatus.LOST_CONFIRMED;
        ResolutionCode = "LOST_CONFIRMED";
        ResolutionNote = Normalize(note);
        ResolvedAt = at;
    }

    public void Close(string? note, DateTimeOffset at)
    {
        if (Status != ParcelIncidentStatus.RESOLVED)
            throw new InvalidOperationException("Only a resolved incident can be closed.");
        Status = ParcelIncidentStatus.CLOSED;
        ResolutionNote = Normalize(note);
        ResolvedAt ??= at;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
