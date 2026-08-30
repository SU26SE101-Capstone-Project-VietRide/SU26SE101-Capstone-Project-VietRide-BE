using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed record DecideParcelStopDepartureApprovalCommand(
    Guid RequestId,
    Guid ReviewerUserId,
    Guid OperatorId,
    string ReviewerRole,
    string Decision,
    string? Note,
    Guid IdempotencyKey) : IRequest<ParcelStopDepartureApprovalResponse>;
