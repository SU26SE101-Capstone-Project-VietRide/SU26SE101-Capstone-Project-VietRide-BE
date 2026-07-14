using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class GetAdminStopHandler(IStopRepository stops) : IRequestHandler<GetAdminStopQuery, StopDto>
{
    public async Task<StopDto> Handle(GetAdminStopQuery request, CancellationToken cancellationToken)
    {
        var stop = await stops.GetByIdAsync(request.StopId, cancellationToken)
            ?? throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        return StopMapper.ToDto(stop);
    }
}
