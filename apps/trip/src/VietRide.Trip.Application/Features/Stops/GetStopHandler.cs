using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class GetStopHandler : IRequestHandler<GetStopQuery, StopDto>
{
    private readonly IStopRepository stopRepository;

    public GetStopHandler(IStopRepository stopRepository)
    {
        this.stopRepository = stopRepository;
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

        return Task.FromResult(StopMapper.ToDto(stop));
    }
}
