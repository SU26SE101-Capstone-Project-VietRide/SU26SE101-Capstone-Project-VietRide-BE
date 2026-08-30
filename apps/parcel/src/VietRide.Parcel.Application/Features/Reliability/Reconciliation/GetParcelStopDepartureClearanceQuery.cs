using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed record GetParcelStopDepartureClearanceQuery(
    Guid TripId,
    Guid StopId,
    Guid OperatorId) : IRequest<ParcelStopDepartureClearanceResponse>;

public sealed record ParcelStopDepartureClearanceResponse(
    Guid TripId,
    Guid StopId,
    Guid OperatorId,
    string Status,
    IReadOnlyList<Guid> UnresolvedParcelIds,
    Guid? ApprovalRequestId,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt);
