using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleTripDisruptedCommandHandler
    : IRequestHandler<HandleTripDisruptedCommand, int>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;

    public HandleTripDisruptedCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
    }

    public async Task<int> Handle(
        HandleTripDisruptedCommand command,
        CancellationToken cancellationToken)
    {
        if (command.HasSubstitution)
        {
            return 0;
        }

        var updated = await _parcelRepository.TryBulkSetPendingOperatorActionByTripIdAsync(
            command.TripId,
            DateTimeOffset.UtcNow,
            cancellationToken);

        foreach (var parcel in updated)
        {
            var refundAmount = CalculateDisruptionRefund(
                parcel.DepositAmount + parcel.AdditionalAmount,
                command.TraveledRatio);
            if (refundAmount > 0)
            {
                await ParcelOutboxEvents.EnqueueRefundAsync(
                    _outbox,
                    parcel.ParcelId,
                    parcel.SenderUserId,
                    refundAmount,
                    parcel.ParcelId.ToString("D"),
                    cancellationToken);
            }

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.PendingOperatorAction,
                new
                {
                    parcelId = parcel.ParcelId,
                    parcelCode = parcel.ParcelCode,
                    operatorId = parcel.OperatorId,
                    userId = parcel.SenderUserId,
                    tripId = parcel.TripId,
                    refundAmount,
                    traveledRatio = command.TraveledRatio,
                },
                cancellationToken);
        }

        return updated.Count;
    }

    private static long CalculateDisruptionRefund(long paidAmount, decimal traveledRatio)
    {
        if (paidAmount <= 0)
        {
            return 0;
        }

        var boundedRatio = Math.Clamp(traveledRatio, 0m, 1m);
        var rawRefund = (long)Math.Floor(paidAmount * (1m - boundedRatio));
        if (rawRefund <= 0)
        {
            return 0;
        }

        return Math.Max(1000L, rawRefund / 1000L * 1000L);
    }
}
