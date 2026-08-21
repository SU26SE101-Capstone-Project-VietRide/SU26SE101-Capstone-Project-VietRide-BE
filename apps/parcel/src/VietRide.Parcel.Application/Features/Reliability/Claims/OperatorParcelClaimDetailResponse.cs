using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record OperatorParcelClaimDetailResponse(
    ParcelClaimResponse Claim,
    ReliabilityParcelSummaryResponse Parcel,
    ReliabilityIncidentSummaryResponse? Incident,
    ReliabilityCustodySummaryResponse? CurrentCustody,
    ReliabilityTripResponse? Trip,
    ReliabilityLocationResponse? ExpectedDropoff,
    OperatorUserSummaryResponse Beneficiary,
    string FundingStatus,
    IReadOnlyList<string> AvailableActions);
