using MediatR;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetOperatorSummaries;

public sealed record GetOperatorSummariesQuery(
    IReadOnlyList<Guid> OperatorIds) : IRequest<IReadOnlyList<OperatorSummaryDto>>;
