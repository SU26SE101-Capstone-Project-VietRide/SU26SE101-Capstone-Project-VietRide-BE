using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed record GetParcelStopDepartureApprovalQuery(
    Guid RequestId,
    Guid UserId,
    Guid OperatorId,
    string Role) : IRequest<ParcelStopDepartureApprovalResponse>;
