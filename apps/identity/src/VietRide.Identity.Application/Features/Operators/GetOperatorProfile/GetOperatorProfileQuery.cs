using MediatR;

namespace VietRide.Identity.Application.Features.Operators;

public sealed record GetOperatorProfileQuery(Guid OperatorId) : IRequest<OperatorProfileResponse>;
