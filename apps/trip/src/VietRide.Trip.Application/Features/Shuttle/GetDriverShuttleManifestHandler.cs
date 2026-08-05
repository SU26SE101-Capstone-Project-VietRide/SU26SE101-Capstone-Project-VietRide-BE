using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class GetDriverShuttleManifestHandler
    : IRequestHandler<GetDriverShuttleManifestQuery, ShuttleDriverManifest>
{
    private readonly IShuttleDispatchService service;

    public GetDriverShuttleManifestHandler(IShuttleDispatchService service)
    {
        this.service = service;
    }

    public Task<ShuttleDriverManifest> Handle(
        GetDriverShuttleManifestQuery request,
        CancellationToken cancellationToken)
        => service.GetDriverManifestAsync(request.ShuttleTripId, request.DriverUserId, cancellationToken);
}
