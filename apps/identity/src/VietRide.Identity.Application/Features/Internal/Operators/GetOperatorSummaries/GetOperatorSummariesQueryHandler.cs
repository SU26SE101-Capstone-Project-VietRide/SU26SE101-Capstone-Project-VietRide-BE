using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetOperatorSummaries;

public sealed class GetOperatorSummariesQueryHandler
    : IRequestHandler<GetOperatorSummariesQuery, IReadOnlyList<OperatorSummaryDto>>
{
    private readonly IOperatorRepository _operators;

    public GetOperatorSummariesQueryHandler(IOperatorRepository operators)
    {
        _operators = operators;
    }

    public async Task<IReadOnlyList<OperatorSummaryDto>> Handle(
        GetOperatorSummariesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OperatorIds.Count == 0)
            return [];

        var operators = await _operators.ListSummariesByIdsAsync(
            request.OperatorIds,
            cancellationToken);
        return operators
            .OrderBy(operatorTenant => operatorTenant.Id)
            .Select(operatorTenant => new OperatorSummaryDto(operatorTenant.Id, operatorTenant.Name))
            .ToArray();
    }
}
