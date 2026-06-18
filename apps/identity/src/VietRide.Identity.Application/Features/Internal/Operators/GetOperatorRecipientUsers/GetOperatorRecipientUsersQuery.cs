using MediatR;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetOperatorRecipientUsers;

public sealed record GetOperatorRecipientUsersQuery(Guid OperatorId) : IRequest<IReadOnlyList<Guid>>;
