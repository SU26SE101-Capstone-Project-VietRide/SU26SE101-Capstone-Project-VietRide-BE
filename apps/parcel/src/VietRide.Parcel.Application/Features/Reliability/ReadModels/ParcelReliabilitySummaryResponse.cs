namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ParcelReliabilitySummaryResponse(
    ReliabilityCustodySummaryResponse? CurrentCustody,
    ReliabilityIncidentSummaryResponse? ActiveIncident,
    ReliabilityClaimSummaryResponse? Claim,
    DateTimeOffset? NextUpdateAt,
    IReadOnlyList<string> AvailableActions);
