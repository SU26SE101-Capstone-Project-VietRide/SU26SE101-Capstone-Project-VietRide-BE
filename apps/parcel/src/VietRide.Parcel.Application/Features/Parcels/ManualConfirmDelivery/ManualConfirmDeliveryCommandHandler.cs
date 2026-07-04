using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;

public sealed class ManualConfirmDeliveryCommandHandler
    : IRequestHandler<ManualConfirmDeliveryCommand, ManualConfirmDeliveryResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public ManualConfirmDeliveryCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _statsRepository = statsRepository;
    }

    public async Task<ManualConfirmDeliveryResponse> Handle(
        ManualConfirmDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        if (parcel.Status != ParcelStatus.DELIVERED_PENDING_CONFIRM)
            throw new BadRequestException("PARCEL_NOT_PENDING_CONFIRM", $"Parcel is in status '{parcel.Status}' and cannot be confirmed.");

        var now = DateTimeOffset.UtcNow;
        var note = command.Note.Trim();
        var snapshot = await _parcelRepository.TryManualConfirmDeliveryAsync(
            command.ParcelId,
            command.OperatorId,
            command.ActorUserId,
            note,
            now,
            cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException("RACE_LOST", $"Parcel '{command.ParcelId}' status changed concurrently; cannot confirm delivery.");

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.DeliveryConfirmed,
            new
            {
                parcelId = snapshot.ParcelId,
                operatorId = snapshot.OperatorId,
                actorUserId = command.ActorUserId,
                manual = true,
                confirmedAt = now,
                note,
            },
            cancellationToken);

        await _statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0, 0, 1, 0, 0, 0, 0,
            cancellationToken);

        return new ManualConfirmDeliveryResponse(snapshot.ParcelId, snapshot.Status.ToString(), now);
    }
}
