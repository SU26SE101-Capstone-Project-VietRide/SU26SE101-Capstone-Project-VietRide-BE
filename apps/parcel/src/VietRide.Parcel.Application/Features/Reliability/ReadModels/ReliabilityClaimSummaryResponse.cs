namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityClaimSummaryResponse(
    Guid ClaimId,
    string Status,
    long TotalAwardVnd,
    DateTimeOffset? DecisionDeadline,
    DateTimeOffset? PayoutDeadline,
    string? SlaState);
