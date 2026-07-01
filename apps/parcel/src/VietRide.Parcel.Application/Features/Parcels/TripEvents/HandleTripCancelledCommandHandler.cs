using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleTripCancelledCommandHandler
    : IRequestHandler<HandleTripCancelledCommand, int>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;

    public HandleTripCancelledCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
    }

    public async Task<int> Handle(
        HandleTripCancelledCommand command,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rejected = await _parcelRepository.TryRejectPreAcceptanceByTripIdAsync(command.TripId, now, cancellationToken);
        var cancelled = await _parcelRepository.TryCancelPendingByTripIdAsync(command.TripId, now, cancellationToken);
        var operatorAction = await _parcelRepository.TryBulkSetPendingOperatorActionByTripIdAsync(command.TripId, now, cancellationToken);

        foreach (var parcel in rejected)
        {
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.Rejected,
                new { parcelId = parcel.ParcelId },
                cancellationToken);
        }

        foreach (var parcel in cancelled)
        {
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.Cancelled,
                new { parcelId = parcel.ParcelId },
                cancellationToken);
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.RefundInitiated,
                new { parcelId = parcel.ParcelId, refundAmount = parcel.DepositAmount + parcel.AdditionalAmount },
                cancellationToken);
        }

        return rejected.Count + cancelled.Count + operatorAction.Count;
    }
}
