using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleTripDisruptedCommandHandler
    : IRequestHandler<HandleTripDisruptedCommand, int>
{
    private readonly IParcelRepository _parcelRepository;

    public HandleTripDisruptedCommandHandler(IParcelRepository parcelRepository)
    {
        _parcelRepository = parcelRepository;
    }

    public async Task<int> Handle(
        HandleTripDisruptedCommand command,
        CancellationToken cancellationToken)
    {
        var updated = await _parcelRepository.TryBulkSetPendingOperatorActionByTripIdAsync(
            command.TripId,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return updated.Count;
    }
}
