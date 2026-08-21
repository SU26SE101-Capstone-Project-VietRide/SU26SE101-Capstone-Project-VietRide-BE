using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Policies;

public sealed record GetParcelCompensationPolicyQuery(Guid OperatorId)
    : IRequest<ParcelCompensationPolicyResponse>;
