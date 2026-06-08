using MediatR;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperator;

public sealed record GetInternalOperatorQuery(Guid OperatorId) : IRequest<InternalOperatorLookupDto>;
