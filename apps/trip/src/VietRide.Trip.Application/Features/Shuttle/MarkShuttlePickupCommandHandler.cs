using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class MarkShuttlePickupCommandHandler
    : IRequestHandler<MarkShuttlePickupCommand, ShuttlePickupResult>
{
    private readonly IShuttleDispatchService _service;

    public MarkShuttlePickupCommandHandler(IShuttleDispatchService service)
    {
        _service = service;
    }

    public Task<ShuttlePickupResult> Handle(
        MarkShuttlePickupCommand request,
        CancellationToken cancellationToken)
        => _service.MarkPickupAsync(
            request.ShuttleTripId,
            request.PickupOrder,
            request.DriverUserId,
            cancellationToken);
}
