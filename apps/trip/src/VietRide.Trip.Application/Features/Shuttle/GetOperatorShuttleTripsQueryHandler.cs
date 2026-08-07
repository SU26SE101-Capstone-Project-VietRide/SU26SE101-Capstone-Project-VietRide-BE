using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class GetOperatorShuttleTripsQueryHandler
    : IRequestHandler<GetOperatorShuttleTripsQuery, PagedResult<OperatorShuttleTripListItemDto>>
{
    private readonly IShuttleDispatchService service;

    public GetOperatorShuttleTripsQueryHandler(IShuttleDispatchService service) => this.service = service;

    public Task<PagedResult<OperatorShuttleTripListItemDto>> Handle(
        GetOperatorShuttleTripsQuery request,
        CancellationToken cancellationToken)
        => service.GetHistoryAsync(
            request.OperatorId,
            request.Page,
            request.PageSize,
            request.From,
            request.To,
            request.Statuses,
            cancellationToken);
}
