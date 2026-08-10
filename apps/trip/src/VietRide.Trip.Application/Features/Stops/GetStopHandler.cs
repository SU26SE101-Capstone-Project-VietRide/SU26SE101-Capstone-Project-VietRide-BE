using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class GetStopHandler : IRequestHandler<GetStopQuery, StopDto>
{
    private readonly ILocationRepository? locationRepository;
    private readonly IStopRepository stopRepository;

    public GetStopHandler(IStopRepository stopRepository, ILocationRepository? locationRepository = null)
    {
        this.stopRepository = stopRepository;
        this.locationRepository = locationRepository;
    }

    public Task<StopDto> Handle(GetStopQuery request, CancellationToken cancellationToken)
    {
        var stop = stopRepository.QueryNoTracking()
            .FirstOrDefault(stop =>
                stop.Id == request.StopId
                && stop.OperatorId == request.OperatorId
                && stop.DeletedAt == null);

        if (stop is null)
        {
            throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        }

        var locations = StopLocationContextResolver.Resolve(locationRepository, [stop]);
        return Task.FromResult(StopMapper.ToDto(stop, locations));
    }
}
