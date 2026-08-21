using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record OperatorParcelClaimListItem(
    Guid ClaimId,
    string Status,
    ReliabilityParcelSummaryResponse Parcel,
    OperatorUserSummaryResponse Sender,
    ReliabilityIncidentSummaryResponse? Incident,
    int EvidenceCount,
    ParcelCompensationPolicySnapshotResponse PolicySnapshot,
    long CargoAwardVnd,
    long FreightRefundVnd,
    long TotalAwardVnd,
    DateTimeOffset? Deadline,
    string? SlaState,
    string FundingStatus,
    ReliabilityTripResponse? Trip,
    IReadOnlyList<string> AvailableActions);
