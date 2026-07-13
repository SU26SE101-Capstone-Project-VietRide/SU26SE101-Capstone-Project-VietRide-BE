using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class GetShuttleRequestsQueryHandler : IRequestHandler<GetShuttleRequestsQuery, ShuttleRequestPage>
{
    private readonly IShuttleDispatchService _service;

    public GetShuttleRequestsQueryHandler(IShuttleDispatchService service)
    {
        _service = service;
    }

    public Task<ShuttleRequestPage> Handle(GetShuttleRequestsQuery request, CancellationToken cancellationToken)
        => _service.GetPendingAsync(request.OperatorId, request.Page, request.PageSize, cancellationToken);
}
