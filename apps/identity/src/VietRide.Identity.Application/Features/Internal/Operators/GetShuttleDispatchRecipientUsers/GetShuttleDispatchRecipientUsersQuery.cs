using MediatR;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetShuttleDispatchRecipientUsers;

public sealed record GetShuttleDispatchRecipientUsersQuery(Guid OperatorId) : IRequest<IReadOnlyList<Guid>>;
