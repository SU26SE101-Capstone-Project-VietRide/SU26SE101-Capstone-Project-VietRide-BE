using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed class ListSubscriptionCustomRequestsQueryHandler
    : IRequestHandler<ListSubscriptionCustomRequestsQuery, IReadOnlyList<SubscriptionCustomRequestDto>>
{
    private readonly ISubscriptionCustomRequestRepository _requests;

    public ListSubscriptionCustomRequestsQueryHandler(ISubscriptionCustomRequestRepository requests)
    {
        _requests = requests;
    }

    public async Task<IReadOnlyList<SubscriptionCustomRequestDto>> Handle(
        ListSubscriptionCustomRequestsQuery query,
        CancellationToken cancellationToken)
    {
        var requests = _requests.QueryNoTracking();
        if (query.OperatorId.HasValue)
            requests = requests.Where(request => request.OperatorId == query.OperatorId.Value);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = Enum.Parse<SubscriptionCustomRequestStatus>(query.Status, ignoreCase: false);
            requests = requests.Where(request => request.Status == status);
        }

        return (await requests.OrderByDescending(request => request.CreatedAt).ToListAsync(cancellationToken))
            .Select(SubscriptionCustomRequestMapper.ToDto)
            .ToArray();
    }
}
