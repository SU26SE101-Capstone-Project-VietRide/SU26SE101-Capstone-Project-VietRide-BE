using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class ReassignShuttleTripCommandHandler
    : IRequestHandler<ReassignShuttleTripCommand, ReassignShuttleTripResult>
{
    private readonly IShuttleDispatchService _service;

    public ReassignShuttleTripCommandHandler(IShuttleDispatchService service)
    {
        _service = service;
    }

    public Task<ReassignShuttleTripResult> Handle(
        ReassignShuttleTripCommand request,
        CancellationToken cancellationToken)
        => _service.ReassignAsync(
            new ReassignShuttleTripInput(
                request.OperatorId,
                request.ActorUserId,
                request.ShuttleTripId,
                request.DriverUserId,
                request.VehicleId,
                request.Reason ?? string.Empty),
            cancellationToken);
}
