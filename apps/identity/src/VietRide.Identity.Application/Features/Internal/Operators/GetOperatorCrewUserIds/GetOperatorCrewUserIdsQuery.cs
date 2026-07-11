using MediatR;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetOperatorCrewUserIds;

public sealed record GetOperatorCrewUserIdsQuery(Guid OperatorId) : IRequest<IReadOnlyList<Guid>>;
