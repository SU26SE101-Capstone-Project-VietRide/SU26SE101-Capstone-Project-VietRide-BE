using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleVehicleSubstitutedCommandHandler
    : IRequestHandler<HandleVehicleSubstitutedCommand, int>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public HandleVehicleSubstitutedCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<int> Handle(
        HandleVehicleSubstitutedCommand command,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var parcels = await _parcelRepository.TryBulkRequestTransferByTripIdAsync(
            command.OldTripId,
            command.NewTripId,
            command.OperatorId,
            now,
            cancellationToken);

        foreach (var parcel in parcels)
        {
            var eventId = ParcelOperationId.Create(
                command.EventId,
                parcel.ParcelId,
                "TRANSFER_INITIATED_EVENT");
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                eventId,
                ParcelOutboxEvents.TransferInitiated,
                new
                {
                    eventId,
                    occurredAt = now,
                    parcelId = parcel.ParcelId,
                    parcelCode = parcel.ParcelCode,
                    operatorId = parcel.OperatorId,
                    userId = parcel.SenderUserId,
                    originalTripId = command.OldTripId,
                    newTripId = command.NewTripId,
                    reason = command.Reason,
                },
                cancellationToken);
        }

        return parcels.Count;
    }
}
