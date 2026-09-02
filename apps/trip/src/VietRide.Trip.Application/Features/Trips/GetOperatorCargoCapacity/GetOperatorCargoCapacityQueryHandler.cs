using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Trips.GetOperatorCargoCapacity;

public sealed class GetOperatorCargoCapacityQueryHandler
    : IRequestHandler<GetOperatorCargoCapacityQuery, OperatorCargoCapacityDto>
{
    private readonly IOperatorCargoCapacityReadRepository repository;

    public GetOperatorCargoCapacityQueryHandler(IOperatorCargoCapacityReadRepository repository)
    {
        this.repository = repository;
    }

    public async Task<OperatorCargoCapacityDto> Handle(
        GetOperatorCargoCapacityQuery request,
        CancellationToken cancellationToken)
    {
        var capacity = await repository.GetAsync(request.TripId, cancellationToken);
        if (capacity is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        }

        if (capacity.OperatorId != request.OperatorId)
        {
            throw new ForbiddenException("FORBIDDEN", "Trip does not belong to this operator.");
        }

        var percentFull = capacity.MaxCargoWeightKg <= 0m
            ? 0m
            : Math.Round(capacity.LoadedWeightKg / capacity.MaxCargoWeightKg * 100m, 2);

        return new OperatorCargoCapacityDto(
            capacity.TripId,
            capacity.ReservedWeightKg,
            capacity.ReservedVolumeM3,
            capacity.LoadedWeightKg,
            capacity.LoadedVolumeM3,
            capacity.MaxCargoWeightKg,
            capacity.MaxCargoVolumeM3,
            Math.Max(0m, capacity.MaxCargoWeightKg - capacity.ReservedWeightKg - capacity.LoadedWeightKg),
            Math.Max(0m, capacity.MaxCargoVolumeM3 - capacity.ReservedVolumeM3 - capacity.LoadedVolumeM3),
            percentFull,
            capacity.HistoricalLoadedWeightKg,
            capacity.HistoricalLoadedVolumeM3);
    }
}
