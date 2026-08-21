namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityIncidentSummaryResponse(
    Guid IncidentId,
    string Type,
    string Status,
    DateTimeOffset SearchDeadline,
    DateTimeOffset? NextUpdateAt,
    string SlaState,
    bool OperatorProcessBreach);
