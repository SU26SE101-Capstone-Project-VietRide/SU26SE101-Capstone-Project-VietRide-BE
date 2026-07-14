using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class GetShuttleTrackingContextQueryHandler
    : IRequestHandler<GetShuttleTrackingContextQuery, ShuttleTrackingContext>
{
    private readonly IShuttleDispatchService _service;

    public GetShuttleTrackingContextQueryHandler(IShuttleDispatchService service)
    {
        _service = service;
    }

    public Task<ShuttleTrackingContext> Handle(
        GetShuttleTrackingContextQuery request,
        CancellationToken cancellationToken)
        => _service.GetTrackingContextAsync(
            request.ShuttleTripId,
            request.UserId,
            request.Role,
            request.OperatorId,
            cancellationToken);
}
