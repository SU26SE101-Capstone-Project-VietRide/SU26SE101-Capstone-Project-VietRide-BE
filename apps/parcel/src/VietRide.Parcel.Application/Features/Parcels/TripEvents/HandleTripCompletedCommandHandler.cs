using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleTripCompletedCommandHandler
    : IRequestHandler<HandleTripCompletedCommand, int>
{
    private readonly IParcelRepository _parcelRepository;

    public HandleTripCompletedCommandHandler(IParcelRepository parcelRepository)
    {
        _parcelRepository = parcelRepository;
    }

    public async Task<int> Handle(
        HandleTripCompletedCommand command,
        CancellationToken cancellationToken)
    {
        return await _parcelRepository.TryBulkSetPendingOperatorActionByTripIdAsync(
            command.TripId,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }
}
