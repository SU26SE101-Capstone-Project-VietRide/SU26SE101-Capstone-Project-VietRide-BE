using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Reliability.Trace;

public sealed record ParcelTraceResponse(
    Guid ParcelId,
    string ParcelCode,
    string ParcelStatus,
    ReliabilityParcelSummaryResponse ParcelSummary,
    ReliabilityOperatorResponse Operator,
    ReliabilityTripResponse Trip,
    ReliabilityLocationResponse DropoffLocation,
    ParcelCurrentCustodyResponse? CurrentCustody,
    ReliabilityIncidentSummaryResponse? ActiveIncident,
    ReliabilityTripResponse? ForwardingTrip,
    ReliabilityClaimSummaryResponse? ClaimSummary,
    IReadOnlyList<string> AvailableActions,
    ParcelTraceTimelineResponse Timeline,
    IReadOnlyList<ParcelIncidentResponse> Incidents,
    DateTimeOffset? NextUpdateAt);

public sealed record ParcelTraceTimelineResponse(
    IReadOnlyList<ParcelCustodyEventResponse> Items,
    string? NextCursor);

public sealed record ParcelCurrentCustodyResponse(
    string LastEventType,
    string? LastLocationType,
    Guid? LastLocationId,
    string? LastLocationSnapshot,
    DateTimeOffset LastConfirmedAt,
    Guid? CurrentTripId,
    Guid? CurrentVehicleId,
    string TrackingConfidence);

public sealed record ParcelCustodyEventResponse(
    Guid EventId,
    string EventType,
    Guid? TripId,
    string? ExpectedLocationType,
    Guid? ExpectedLocationId,
    string? ActualLocationType,
    Guid? ActualLocationId,
    string? LocationSnapshot,
    DateTimeOffset OccurredAt,
    string ActorRole,
    string Source,
    string? Reason,
    int Sequence);

public sealed record ParcelIncidentResponse(
    Guid IncidentId,
    string Type,
    string Status,
    string? LastKnownLocation,
    DateTimeOffset SearchDeadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    bool OperatorProcessBreach);
