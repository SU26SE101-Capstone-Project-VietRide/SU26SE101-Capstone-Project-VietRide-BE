using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class GetAdminStopHandler : IRequestHandler<GetAdminStopQuery, StopDto>
{
    private readonly ILocationRepository? locations;
    private readonly IStopRepository stops;

    public GetAdminStopHandler(IStopRepository stops, ILocationRepository? locations = null)
    {
        this.stops = stops;
        this.locations = locations;
    }

    public async Task<StopDto> Handle(GetAdminStopQuery request, CancellationToken cancellationToken)
    {
        var stop = await stops.GetByIdAsync(request.StopId, cancellationToken)
            ?? throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        var locationContexts = StopLocationContextResolver.Resolve(locations, [stop]);
        return StopMapper.ToDto(stop, locationContexts);
    }
}
