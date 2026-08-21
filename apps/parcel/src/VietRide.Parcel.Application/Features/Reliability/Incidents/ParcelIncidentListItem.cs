namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

using VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ParcelIncidentListItem(
    Guid IncidentId,
    Guid ParcelId,
    Guid OperatorId,
    string Type,
    string Status,
    Guid? TripId,
    string? LastKnownLocation,
    DateTimeOffset SearchDeadline,
    DateTimeOffset CreatedAt,
    bool OperatorProcessBreach,
    ReliabilityParcelSummaryResponse? Parcel = null,
    ReliabilityTripResponse? Trip = null,
    ReliabilityLocationResponse? ExpectedDropoff = null,
    ReliabilityCustodySummaryResponse? LastCustody = null,
    OperatorUserSummaryResponse? Reporter = null,
    ParcelIncidentTaskSummaryResponse? TaskSummary = null,
    ReliabilityClaimSummaryResponse? ClaimSummary = null,
    ParcelIncidentSlaResponse? Sla = null,
    IReadOnlyList<string>? AvailableActions = null);

public sealed record OperatorUserSummaryResponse(
    Guid? UserId,
    string? DisplayName,
    string? Phone,
    string? Email,
    string? AvatarUrl,
    string? Source = null);

public sealed record ParcelIncidentTaskSummaryResponse(
    int Completed,
    int Total,
    IReadOnlyList<OperatorUserSummaryResponse> Assignees);

public sealed record ParcelIncidentSlaResponse(
    DateTimeOffset Deadline,
    long RemainingMinutes,
    string State);
