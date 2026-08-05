using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Domain.Constants;

namespace VietRide.Trip.Domain.Entities;

public sealed class RouteChangeProposal : BaseEntity<Guid>
{
    private readonly List<RouteChangeProposalStop> stops = [];

    public Guid TripId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid ProposedByUserId { get; private set; }
    public RouteChangeProposalType Type { get; private set; }
    public RouteChangeProposalStatus Status { get; private set; }
    public Guid? SourceAlternativeRouteId { get; private set; }
    public DateTimeOffset? SourceUpdatedAt { get; private set; }
    public Guid? IncidentId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid DestinationStationId { get; private set; }
    public decimal? TotalDistanceKm { get; private set; }
    public int? EstimatedDurationMinutes { get; private set; }
    public string? PathPolyline { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? SupersededByProposalId { get; private set; }
    public Guid? ApprovedAlternativeRouteId { get; private set; }
    public string? ResolutionCode { get; private set; }
    public IReadOnlyCollection<RouteChangeProposalStop> Stops => stops.AsReadOnly();

    private RouteChangeProposal() { }

    public static RouteChangeProposal Create(
        Guid tripId,
        Guid operatorId,
        Guid proposedByUserId,
        RouteChangeProposalType type,
        Guid? sourceAlternativeRouteId,
        DateTimeOffset? sourceUpdatedAt,
        Guid? incidentId,
        string reason,
        string name,
        string? description,
        Guid destinationStationId,
        decimal? totalDistanceKm,
        int? estimatedDurationMinutes,
        string? pathPolyline)
    {
        ValidateGuid(tripId, nameof(tripId));
        ValidateGuid(operatorId, nameof(operatorId));
        ValidateGuid(proposedByUserId, nameof(proposedByUserId));
        ValidateGuid(destinationStationId, nameof(destinationStationId));
        ValidateOptionalGuid(incidentId, nameof(incidentId));
        var normalizedReason = NormalizeRequired(reason, 500, nameof(reason));
        var normalizedName = NormalizeRequired(name, 255, nameof(name));
        var normalizedDescription = NormalizeOptional(description);
        if (totalDistanceKm < 0m)
            throw new ArgumentOutOfRangeException(nameof(totalDistanceKm));
        if (estimatedDurationMinutes < 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedDurationMinutes));
        if (type == RouteChangeProposalType.EXISTING)
        {
            ValidateOptionalGuid(sourceAlternativeRouteId, nameof(sourceAlternativeRouteId));
            if (!sourceAlternativeRouteId.HasValue || !sourceUpdatedAt.HasValue)
                throw new ArgumentException("Existing proposals require source identity and version.");
        }
        else if (sourceAlternativeRouteId.HasValue || sourceUpdatedAt.HasValue)
        {
            throw new ArgumentException("Custom proposals cannot reference an existing alternative route.");
        }

        return new RouteChangeProposal
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            OperatorId = operatorId,
            ProposedByUserId = proposedByUserId,
            Type = type,
            Status = RouteChangeProposalStatus.PENDING,
            SourceAlternativeRouteId = sourceAlternativeRouteId,
            SourceUpdatedAt = sourceUpdatedAt,
            IncidentId = incidentId,
            Reason = normalizedReason,
            Name = normalizedName,
            Description = normalizedDescription,
            DestinationStationId = destinationStationId,
            TotalDistanceKm = totalDistanceKm,
            EstimatedDurationMinutes = estimatedDurationMinutes,
            PathPolyline = NormalizeOptional(pathPolyline),
        };
    }

    public void AddStop(RouteChangeProposalStop stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        if (stop.ProposalId != Id)
            throw new ArgumentException("Stop belongs to another proposal.", nameof(stop));
        if (stops.Any(existing => existing.StopId == stop.StopId || existing.OrderIndex == stop.OrderIndex))
            throw new InvalidOperationException("Proposal stop and order index must be unique.");
        stops.Add(stop);
    }

    public void Approve(Guid actorUserId, Guid approvedAlternativeRouteId, DateTimeOffset decidedAt)
    {
        EnsurePending();
        ValidateGuid(actorUserId, nameof(actorUserId));
        ValidateGuid(approvedAlternativeRouteId, nameof(approvedAlternativeRouteId));
        Status = RouteChangeProposalStatus.APPROVED;
        DecidedByUserId = actorUserId;
        DecidedAt = decidedAt;
        ApprovedAlternativeRouteId = approvedAlternativeRouteId;
    }

    public void Reject(Guid actorUserId, DateTimeOffset decidedAt, string? rejectionReason)
    {
        EnsurePending();
        ValidateGuid(actorUserId, nameof(actorUserId));
        var normalized = NormalizeOptional(rejectionReason);
        if (normalized?.Length > 500)
            throw new ArgumentException("Rejection reason cannot exceed 500 characters.", nameof(rejectionReason));
        Status = RouteChangeProposalStatus.REJECTED;
        DecidedByUserId = actorUserId;
        DecidedAt = decidedAt;
        RejectionReason = normalized;
    }

    public void Supersede(Guid? actorUserId, Guid? approvedProposalId, string resolutionCode, DateTimeOffset decidedAt)
    {
        EnsurePending();
        ValidateOptionalGuid(actorUserId, nameof(actorUserId));
        ValidateOptionalGuid(approvedProposalId, nameof(approvedProposalId));
        if (resolutionCode is not (RouteChangeProposalResolutionCode.AnotherProposalApproved or RouteChangeProposalResolutionCode.RouteChangedDirectly))
            throw new ArgumentOutOfRangeException(nameof(resolutionCode));
        Status = RouteChangeProposalStatus.SUPERSEDED;
        DecidedByUserId = actorUserId;
        DecidedAt = decidedAt;
        SupersededByProposalId = approvedProposalId;
        ResolutionCode = resolutionCode;
    }

    public void Expire(string resolutionCode, DateTimeOffset decidedAt)
    {
        EnsurePending();
        if (resolutionCode is not (RouteChangeProposalResolutionCode.TripNoLongerEditable or RouteChangeProposalResolutionCode.SourceRouteChanged))
            throw new ArgumentOutOfRangeException(nameof(resolutionCode));
        Status = RouteChangeProposalStatus.EXPIRED;
        DecidedAt = decidedAt;
        ResolutionCode = resolutionCode;
    }

    private void EnsurePending()
    {
        if (Status != RouteChangeProposalStatus.PENDING)
            throw new InvalidOperationException("Only pending route-change proposals can transition.");
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
            throw new ArgumentException($"Value must contain between 1 and {maxLength} characters.", parameterName);
        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Value cannot be empty.", parameterName);
    }

    private static void ValidateOptionalGuid(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Value cannot be empty.", parameterName);
    }
}
