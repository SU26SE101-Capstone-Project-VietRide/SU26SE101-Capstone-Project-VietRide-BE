using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Parcels.RejectDelivery;

public sealed class RejectDeliveryCommandHandler
    : IRequestHandler<RejectDeliveryCommand, RejectDeliveryResponse>
{
    private readonly IParcelRepository _parcelRepository;

    public RejectDeliveryCommandHandler(IParcelRepository parcelRepository)
    {
        _parcelRepository = parcelRepository;
    }

    public async Task<RejectDeliveryResponse> Handle(
        RejectDeliveryCommand command,
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

        if (parcel.Status != ParcelStatus.DELIVERED_PENDING_CONFIRM)
            throw new BadRequestException(
                "PARCEL_NOT_PENDING_CONFIRM",
                $"Parcel is in status '{parcel.Status}' and cannot be rejected.");

        if (string.IsNullOrWhiteSpace(command.RejectionReason) || command.RejectionReason.Length > 500)
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Rejection reason is required and must be at most 500 characters.");

        var snapshot = await _parcelRepository.TryRejectDeliveryAsync(
            parcel.Id, command.DeliveryToken, command.RejectionReason, now, cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException(
                "RACE_LOST",
                $"Parcel '{parcel.Id}' status changed concurrently; cannot reject delivery.");

        var rejectedAt = now;
        return new RejectDeliveryResponse(
            snapshot.ParcelId,
            snapshot.Status.ToString(),
            rejectedAt,
            rejectedAt.AddMinutes(15));
    }
}
