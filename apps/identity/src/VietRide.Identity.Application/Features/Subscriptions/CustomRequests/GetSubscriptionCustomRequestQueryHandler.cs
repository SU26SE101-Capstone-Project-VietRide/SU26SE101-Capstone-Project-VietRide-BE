using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class GetSubscriptionCustomRequestQueryHandler
    : IRequestHandler<GetSubscriptionCustomRequestQuery, SubscriptionCustomRequestDto>
{
    private readonly ISubscriptionCustomRequestRepository _requests;

    public GetSubscriptionCustomRequestQueryHandler(ISubscriptionCustomRequestRepository requests)
    {
        _requests = requests;
    }

    public async Task<SubscriptionCustomRequestDto> Handle(
        GetSubscriptionCustomRequestQuery query,
        CancellationToken cancellationToken)
    {
        var request = await _requests.GetByIdAsync(query.RequestId, cancellationToken);
        if (request is null || query.OperatorId.HasValue && request.OperatorId != query.OperatorId.Value)
            throw new NotFoundException(nameof(SubscriptionCustomRequest), query.RequestId);
        return SubscriptionCustomRequestMapper.ToDto(request);
    }
}
