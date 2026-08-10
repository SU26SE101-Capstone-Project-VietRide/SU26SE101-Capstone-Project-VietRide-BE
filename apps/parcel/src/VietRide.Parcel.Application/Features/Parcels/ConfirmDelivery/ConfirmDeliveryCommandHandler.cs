using MediatR;
using VietRide.Parcel.Application.Abstractions.Caching;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
namespace VietRide.Parcel.Application.Features.Parcels.ConfirmDelivery;

public sealed class ConfirmDeliveryCommandHandler
    : IRequestHandler<ConfirmDeliveryCommand, ConfirmDeliveryResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IParcelDeliveryTokenRepository _deliveryTokenRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly IDeliveryConfirmationRateLimiter _rateLimiter;

    public ConfirmDeliveryCommandHandler(
        IParcelRepository parcelRepository,
        IParcelDeliveryTokenRepository deliveryTokenRepository,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository,
        IDeliveryConfirmationRateLimiter rateLimiter)
    {
        _parcelRepository = parcelRepository;
        _deliveryTokenRepository = deliveryTokenRepository;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _rateLimiter = rateLimiter;
    }

    public async Task<ConfirmDeliveryResponse> Handle(
        ConfirmDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        var tokenHash = DeliveryTokenHasher.Hash(command.DeliveryToken);
        await EnsureRateLimitAsync(tokenHash, cancellationToken);

        var deliveryToken = await _deliveryTokenRepository.FindByTokenHashAsync(
            tokenHash,
            cancellationToken);

        if (deliveryToken is null)
            throw new BadRequestException(
                "PARCEL_DELIVERY_TOKEN_INVALID",
                "The delivery token is invalid.");

        var now = DateTimeOffset.UtcNow;

        if (deliveryToken.ExpiresAt <= now)
            throw new BadRequestException(
                "PARCEL_DELIVERY_TOKEN_EXPIRED",
                "The delivery token has expired.");

        if (deliveryToken.RevokedAt is not null)
            throw new BadRequestException(
                "PARCEL_DELIVERY_TOKEN_REVOKED",
                "The delivery token has been revoked.");

        var parcel = await _parcelRepository.GetByIdAsync(
            deliveryToken.ParcelId,
            cancellationToken);
        if (parcel is null)
            throw new BadRequestException(
                "PARCEL_DELIVERY_TOKEN_INVALID",
                "The delivery token is invalid.");

        if (parcel.Status != ParcelStatus.DELIVERED_PENDING_CONFIRM)
            throw new BadRequestException(
                "PARCEL_NOT_PENDING_CONFIRM",
                $"Parcel is in status '{parcel.Status}' and cannot be confirmed.");


        var snapshot = await _parcelRepository.TryConfirmDeliveryAsync(
            parcel.Id, deliveryToken.Id, command.IpAddress, now, cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException(
                "RACE_LOST",
                $"Parcel '{parcel.Id}' status changed concurrently; cannot confirm delivery.");

        if (!await _deliveryTokenRepository.RevokeAsync(
                deliveryToken.Id,
                now,
                cancellationToken))
        {
            throw new CodedConflictException(
                "RACE_LOST",
                $"Parcel '{parcel.Id}' delivery token changed concurrently; cannot confirm delivery.");
        }

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.DeliveryConfirmed,
            new { parcelId = snapshot.ParcelId },
            cancellationToken);

        await _statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            VietRide.Shared.Kernel.Time.BusinessTime.ToLocalDate(now),
            0, 0, 1, 0, 0, 0, 0,
            cancellationToken);

        return new ConfirmDeliveryResponse(snapshot.ParcelId, snapshot.Status.ToString(), now);
    }

    private async Task EnsureRateLimitAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        if (!await _rateLimiter.TryAcquireAsync(tokenHash, cancellationToken))
        {
            throw new TooManyRequestsException(
                "RATE_LIMITED",
                "Too many delivery confirmation attempts. Please try again later.");
        }
    }
}
