using MediatR;

namespace VietRide.Identity.Application.Features.Internal.Operators.SearchOperatorCrew;

public sealed record SearchOperatorCrewQuery(Guid OperatorId, string Search)
    : IRequest<IReadOnlyList<InternalOperatorCrewDto>>;
