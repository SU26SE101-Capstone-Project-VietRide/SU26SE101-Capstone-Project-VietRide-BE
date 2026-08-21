using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class ListAdminSubscriptionCustomRequestsQueryHandler
    : IRequestHandler<ListAdminSubscriptionCustomRequestsQuery, IReadOnlyList<AdminSubscriptionCustomRequestDto>>
{
    private readonly ISubscriptionCustomRequestRepository _requests;
    private readonly IOperatorRepository _operators;

    public ListAdminSubscriptionCustomRequestsQueryHandler(
        ISubscriptionCustomRequestRepository requests,
        IOperatorRepository operators)
    {
        _requests = requests;
        _operators = operators;
    }

    public async Task<IReadOnlyList<AdminSubscriptionCustomRequestDto>> Handle(
        ListAdminSubscriptionCustomRequestsQuery query,
        CancellationToken cancellationToken)
    {
        SubscriptionCustomRequestStatus? status = string.IsNullOrWhiteSpace(query.Status)
            ? null
            : Enum.Parse<SubscriptionCustomRequestStatus>(query.Status, ignoreCase: false);
        var requests = await _requests.ListForAdminAsync(status, cancellationToken);
        var operatorIds = requests.Select(request => request.OperatorId).Distinct().ToArray();
        var operators = await _operators.ListSummariesByIdsAsync(operatorIds, cancellationToken);
        var operatorNames = operators.ToDictionary(operatorTenant => operatorTenant.Id, operatorTenant => operatorTenant.Name);

        return requests.Select(request => AdminSubscriptionCustomRequestMapper.ToDto(
                request,
                GetOperatorName(operatorNames, request.OperatorId)))
            .ToArray();
    }

    private static string GetOperatorName(IReadOnlyDictionary<Guid, string> operatorNames, Guid operatorId)
        => operatorNames.TryGetValue(operatorId, out var operatorName)
            ? operatorName
            : throw new InvalidOperationException($"Operator {operatorId} referenced by a custom request was not found.");
}
