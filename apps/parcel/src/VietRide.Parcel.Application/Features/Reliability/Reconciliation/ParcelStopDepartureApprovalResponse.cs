namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed record ParcelStopDepartureApprovalResponse(
    Guid RequestId,
    Guid TripId,
    Guid StopId,
    Guid OperatorId,
    IReadOnlyList<Guid> UnresolvedParcelIds,
    string DepartureOverrideReason,
    string Status,
    Guid RequestedByUserId,
    string RequestedByRole,
    DateTimeOffset RequestedAt,
    Guid? ReviewedByUserId,
    string? ReviewedByRole,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote,
    IReadOnlyList<string> AvailableActions);
