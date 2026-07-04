using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleVehicleSubstitutedCommandHandler
    : IRequestHandler<HandleVehicleSubstitutedCommand, int>
{
    private readonly IParcelRepository parcelRepository;
    private readonly IIntegrationEventOutbox outbox;

    public HandleVehicleSubstitutedCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox)
    {
        this.parcelRepository = parcelRepository;
        this.outbox = outbox;
    }

    public async Task<int> Handle(HandleVehicleSubstitutedCommand command, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var parcels = await parcelRepository.TryBulkRequestTransferByTripIdAsync(
            command.OldTripId,
            command.NewTripId,
            now,
            cancellationToken);

        foreach (var parcel in parcels)
        {
            await ParcelOutboxEvents.EnqueueAsync(
                outbox,
                ParcelOutboxEvents.TransferInitiated,
                new
                {
                    parcelId = parcel.ParcelId,
                    parcelCode = parcel.ParcelCode,
                    operatorId = parcel.OperatorId,
                    userId = parcel.SenderUserId,
                    oldTripId = command.OldTripId,
                    targetTripId = command.NewTripId,
                    reason = command.Reason,
                },
                cancellationToken);
        }

        return parcels.Count;
    }
}
