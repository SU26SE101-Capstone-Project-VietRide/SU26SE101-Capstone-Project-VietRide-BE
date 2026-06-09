using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Stops;

public sealed class GetStopByIdHandler : IRequestHandler<GetStopByIdQuery, InternalStopDto>
{
    private readonly IStopRepository stopRepository;

    public GetStopByIdHandler(IStopRepository stopRepository)
    {
        this.stopRepository = stopRepository;
    }

    public async Task<InternalStopDto> Handle(GetStopByIdQuery request, CancellationToken cancellationToken)
    {
        var stop = await stopRepository.GetByIdAsync(request.Id, cancellationToken);
        if (stop is null)
        {
            throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        }

        return new InternalStopDto(
            stop.Id,
            stop.OperatorId,
            stop.Name,
            stop.Description,
            stop.Latitude,
            stop.Longitude,
            stop.Address,
            stop.GooglePlaceId,
            stop.IsActive,
            stop.CreatedAt,
            stop.UpdatedAt);
    }
}
