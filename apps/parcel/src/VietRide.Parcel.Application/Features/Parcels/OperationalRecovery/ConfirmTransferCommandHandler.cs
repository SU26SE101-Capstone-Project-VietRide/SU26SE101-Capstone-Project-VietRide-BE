using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed class ConfirmTransferCommandHandler
    : IRequestHandler<ConfirmTransferCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public ConfirmTransferCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _statsRepository = statsRepository;
    }

    public async Task<OperationalParcelResponse> Handle(
        ConfirmTransferCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.Status != ParcelStatus.PENDING_TRANSFER_CONFIRM)
            throw new CodedConflictException("INVALID_TRANSITION", $"Parcel status '{parcel.Status}' cannot confirm transfer.");

        if (parcel.TransferTargetTripId != command.TargetTripId)
            throw new CodedConflictException("INVALID_TRANSFER_TARGET", "Transfer target trip does not match the pending transfer.");

        if (parcel.ParcelCode != command.ParcelCode)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        var now = DateTimeOffset.UtcNow;
        var snapshot = await _parcelRepository.TryConfirmTransferAsync(
            command.ParcelId,
            command.TargetTripId,
            command.ParcelCode,
            command.ConfirmedByUserId,
            now,
            cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException("RACE_LOST", "Parcel status changed concurrently; cannot confirm transfer.");

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.Loaded,
            new { parcelId = snapshot.ParcelId, tripId = snapshot.TripId, actualWeightKg = parcel.ActualWeightKg ?? parcel.EstimatedWeightKg },
            cancellationToken);

        await _statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0, 1, 0, 0, 0, 0, 0,
            cancellationToken);

        return new OperationalParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            TripId: snapshot.TripId,
            TransferConfirmedAt: now);
    }
}
