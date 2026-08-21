using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record GetOperatorParcelClaimDetailQuery(Guid ClaimId, Guid OperatorId)
    : IRequest<OperatorParcelClaimDetailResponse>;
