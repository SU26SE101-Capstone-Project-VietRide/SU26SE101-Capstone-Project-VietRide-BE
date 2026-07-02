using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Parcels.ConfirmDelivery;

public sealed class ConfirmDeliveryCommandHandler
    : IRequestHandler<ConfirmDeliveryCommand, ConfirmDeliveryResponse>
{
    private static readonly TimeSpan DeliveryRejectedUndoWindow = TimeSpan.FromMinutes(15);

    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public ConfirmDeliveryCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _statsRepository = statsRepository;
    }

    public async Task<ConfirmDeliveryResponse> Handle(
        ConfirmDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.FindByDeliveryTokenAsync(
            command.DeliveryToken, cancellationToken);

        if (parcel is null)
            throw new BadRequestException(
                "PARCEL_DELIVERY_TOKEN_INVALID",
                "The delivery token is invalid.");

        var now = DateTimeOffset.UtcNow;

        if (parcel.DeliveryTokenExpiresAt.HasValue && parcel.DeliveryTokenExpiresAt.Value < now)
            throw new BadRequestException(
                "PARCEL_DELIVERY_TOKEN_EXPIRED",
                "The delivery token has expired.");

        if (parcel.DeliveryTokenRevokedAt is not null)
            throw new BadRequestException(
                "PARCEL_DELIVERY_TOKEN_REVOKED",
                "The delivery token has been revoked.");

        if (parcel.Status != ParcelStatus.DELIVERED_PENDING_CONFIRM &&
            parcel.Status != ParcelStatus.DELIVERY_REJECTED)
            throw new BadRequestException(
                "PARCEL_NOT_PENDING_CONFIRM",
                $"Parcel is in status '{parcel.Status}' and cannot be confirmed.");

        var wasRejected = parcel.Status == ParcelStatus.DELIVERY_REJECTED;
        if (wasRejected && (!parcel.RejectedAt.HasValue || parcel.RejectedAt.Value.Add(DeliveryRejectedUndoWindow) <= now))
            throw new BadRequestException(
                "PARCEL_DELIVERY_REJECTED_WINDOW_EXPIRED",
                "The delivery rejection undo window has expired.");

        var snapshot = await _parcelRepository.TryConfirmDeliveryAsync(
            parcel.Id, command.DeliveryToken, command.IpAddress, now, cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException(
                "RACE_LOST",
                $"Parcel '{parcel.Id}' status changed concurrently; cannot confirm delivery.");

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.DeliveryConfirmed,
            new { parcelId = snapshot.ParcelId },
            cancellationToken);

        await _statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0, 0, 1, wasRejected ? -1 : 0, 0, 0, 0,
            cancellationToken);

        return new ConfirmDeliveryResponse(snapshot.ParcelId, snapshot.Status.ToString(), now);
    }
}
