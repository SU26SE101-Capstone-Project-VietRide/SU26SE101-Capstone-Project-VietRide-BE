using MediatR;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Application.Features.Reliability.Trace;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record GetParcelIncidentDetailQuery(
    Guid IncidentId,
    Guid OperatorId,
    int? BeforeSequence = null,
    int Limit = 50)
    : IRequest<ParcelIncidentDetailResponse>;

public sealed record ParcelIncidentDetailResponse(
    ParcelIncidentListItem Incident,
    IReadOnlyList<ParcelSearchTaskResponse> SearchTasks,
    string? ExpectedLocation,
    string? ResolutionCode,
    string? ResolutionNote,
    DateTimeOffset? ResolvedAt,
    ParcelCurrentCustodyResponse? CurrentCustody,
    ParcelIncidentCustodyTimelineResponse CustodyTimeline,
    ParcelClaimResponse? Claim,
    ReliabilityParcelSummaryResponse? Parcel = null,
    OperatorUserSummaryResponse? Sender = null,
    OperatorUserSummaryResponse? Recipient = null,
    ReliabilityTripResponse? Trip = null,
    ReliabilityLocationResponse? ExpectedDropoff = null,
    OperatorUserSummaryResponse? Reporter = null,
    ReliabilityTripResponse? ForwardingSummary = null,
    IReadOnlyList<string>? AvailableActions = null,
    ParcelForwardingOperationResponse? ForwardingOperation = null);

public sealed record ParcelForwardingOperationResponse(
    ReliabilityTripResponse TargetTrip,
    ParcelTransitLegResponse? NewLeg,
    string CargoTransferStatus,
    string NextHandoffAction);

public sealed record ParcelTransitLegResponse(
    Guid LegId,
    Guid TripId,
    int Sequence,
    string Status,
    Guid? ExpectedOriginId,
    Guid? ExpectedDestinationId,
    string? ExpectedOriginName,
    string? ExpectedDestinationName,
    Guid? VehicleId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt);

public sealed record ParcelIncidentCustodyTimelineResponse(
    IReadOnlyList<ParcelIncidentCustodyEventResponse> Items,
    string? NextCursor);

public sealed record ParcelIncidentCustodyEventResponse(
    Guid EventId,
    string EventType,
    Guid? LegId,
    Guid? TripId,
    string? ExpectedLocationType,
    Guid? ExpectedLocationId,
    string? ActualLocationType,
    Guid? ActualLocationId,
    string? LocationSnapshot,
    Guid? VehicleId,
    Guid? ActorId,
    string ActorRole,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string Source,
    IReadOnlyList<string> EvidenceReferences,
    string? Reason,
    int Sequence);
