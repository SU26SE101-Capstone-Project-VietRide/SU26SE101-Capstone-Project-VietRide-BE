using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class RouteChangeProposalIntegrationEvent : IntegrationEventBase
{
    public const string Created = "trip.route_change_proposal.created";
    public const string Approved = "trip.route_change_proposal.approved";
    public const string Rejected = "trip.route_change_proposal.rejected";
    public const string Superseded = "trip.route_change_proposal.superseded";
    public const string Expired = "trip.route_change_proposal.expired";

    public RouteChangeProposalIntegrationEvent(
        string eventType,
        Guid proposalId,
        Guid tripId,
        Guid operatorId,
        Guid proposedByUserId,
        Guid? actorUserId,
        string proposalType,
        string status,
        Guid? sourceAlternativeRouteId,
        Guid? approvedAlternativeRouteId,
        Guid? incidentId,
        string reason,
        string? rejectionReason,
        string? resolutionCode,
        Guid? supersededByProposalId,
        DateTimeOffset occurredAt)
        : base(Guid.NewGuid(), occurredAt.UtcDateTime)
    {
        if (eventType is not (Created or Approved or Rejected or Superseded or Expired))
            throw new ArgumentOutOfRangeException(nameof(eventType));
        EventTypeValue = eventType;
        ProposalId = proposalId;
        TripId = tripId;
        OperatorId = operatorId;
        ProposedByUserId = proposedByUserId;
        ActorUserId = actorUserId;
        ProposalType = proposalType;
        Status = status;
        SourceAlternativeRouteId = sourceAlternativeRouteId;
        ApprovedAlternativeRouteId = approvedAlternativeRouteId;
        IncidentId = incidentId;
        Reason = reason;
        RejectionReason = rejectionReason;
        ResolutionCode = resolutionCode;
        SupersededByProposalId = supersededByProposalId;
    }

    [JsonIgnore]
    public string EventTypeValue { get; }
    public Guid ProposalId { get; }
    public Guid TripId { get; }
    public Guid OperatorId { get; }
    public Guid ProposedByUserId { get; }
    public Guid? ActorUserId { get; }
    public string ProposalType { get; }
    public string Status { get; }
    public Guid? SourceAlternativeRouteId { get; }
    public Guid? ApprovedAlternativeRouteId { get; }
    public Guid? IncidentId { get; }
    public string Reason { get; }
    public string? RejectionReason { get; }
    public string? ResolutionCode { get; }
    public Guid? SupersededByProposalId { get; }
    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
