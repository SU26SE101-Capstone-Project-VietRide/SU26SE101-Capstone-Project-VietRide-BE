using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class GetAdminSubscriptionCustomRequestQueryHandler
    : IRequestHandler<GetAdminSubscriptionCustomRequestQuery, AdminSubscriptionCustomRequestDto>
{
    private readonly ISubscriptionCustomRequestRepository _requests;
    private readonly IOperatorRepository _operators;

    public GetAdminSubscriptionCustomRequestQueryHandler(
        ISubscriptionCustomRequestRepository requests,
        IOperatorRepository operators)
    {
        _requests = requests;
        _operators = operators;
    }

    public async Task<AdminSubscriptionCustomRequestDto> Handle(
        GetAdminSubscriptionCustomRequestQuery query,
        CancellationToken cancellationToken)
    {
        var request = await _requests.GetByIdAsync(query.RequestId, cancellationToken)
            ?? throw new NotFoundException(nameof(SubscriptionCustomRequest), query.RequestId);
        var operators = await _operators.ListSummariesByIdsAsync([request.OperatorId], cancellationToken);
        var operatorTenant = operators.SingleOrDefault(operatorTenant => operatorTenant.Id == request.OperatorId)
            ?? throw new InvalidOperationException(
                $"Operator {request.OperatorId} referenced by custom request {request.Id} was not found.");

        return AdminSubscriptionCustomRequestMapper.ToDto(request, operatorTenant.Name);
    }
}
