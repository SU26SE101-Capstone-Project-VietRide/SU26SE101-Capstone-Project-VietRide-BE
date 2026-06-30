using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Parcels.ConfirmDelivery;

public sealed class ConfirmDeliveryCommandHandler
    : IRequestHandler<ConfirmDeliveryCommand, ConfirmDeliveryResponse>
{
    private readonly IParcelRepository _parcelRepository;

    public ConfirmDeliveryCommandHandler(IParcelRepository parcelRepository)
    {
        _parcelRepository = parcelRepository;
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

        var snapshot = await _parcelRepository.TryConfirmDeliveryAsync(
            parcel.Id, command.DeliveryToken, command.IpAddress, now, cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException(
                "RACE_LOST",
                $"Parcel '{parcel.Id}' status changed concurrently; cannot confirm delivery.");

        return new ConfirmDeliveryResponse(snapshot.ParcelId, snapshot.Status.ToString(), now);
    }
}
