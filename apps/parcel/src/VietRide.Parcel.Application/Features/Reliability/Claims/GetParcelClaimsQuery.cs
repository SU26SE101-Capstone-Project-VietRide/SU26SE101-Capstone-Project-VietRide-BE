using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed record GetParcelClaimsQuery(
    Guid ParcelId,
    Guid UserId,
    Guid? OperatorId) : IRequest<IReadOnlyList<ParcelClaimResponse>>;
