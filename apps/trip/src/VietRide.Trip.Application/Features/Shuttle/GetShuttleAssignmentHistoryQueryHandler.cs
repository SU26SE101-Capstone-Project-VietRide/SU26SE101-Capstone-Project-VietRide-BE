using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class GetShuttleAssignmentHistoryQueryHandler
    : IRequestHandler<GetShuttleAssignmentHistoryQuery, PagedResult<ShuttleAssignmentHistoryItemDto>>
{
    private readonly IShuttleDispatchService _service;

    public GetShuttleAssignmentHistoryQueryHandler(IShuttleDispatchService service)
    {
        _service = service;
    }

    public Task<PagedResult<ShuttleAssignmentHistoryItemDto>> Handle(
        GetShuttleAssignmentHistoryQuery request,
        CancellationToken cancellationToken)
        => _service.GetAssignmentHistoryAsync(
            request.OperatorId,
            request.ShuttleTripId,
            request.Page,
            request.PageSize,
            cancellationToken);
}
