using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleTripStartedCommandHandler
    : IRequestHandler<HandleTripStartedCommand, int>
{
    private readonly IParcelRepository _parcelRepository;

    public HandleTripStartedCommandHandler(IParcelRepository parcelRepository)
    {
        _parcelRepository = parcelRepository;
    }

    public async Task<int> Handle(
        HandleTripStartedCommand command,
        CancellationToken cancellationToken)
    {
        var updated = await _parcelRepository.TryBulkSetInTransitByTripIdAsync(
            command.TripId,
            command.ActualDepartureTime,
            cancellationToken);

        return updated.Count;
    }
}
