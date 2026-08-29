using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record GetParcelClaimAppealQuery(
    Guid AppealId,
    Guid OperatorId) : IRequest<ParcelClaimAppealResponse>;
