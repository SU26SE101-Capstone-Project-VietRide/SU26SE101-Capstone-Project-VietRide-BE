using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;

public sealed class ManualConfirmDeliveryCommandHandler
    : IRequestHandler<ManualConfirmDeliveryCommand, ManualConfirmDeliveryResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IParcelDeliveryTokenRepository _deliveryTokenRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public ManualConfirmDeliveryCommandHandler(
        IParcelRepository parcelRepository,
        IParcelDeliveryTokenRepository deliveryTokenRepository,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _deliveryTokenRepository = deliveryTokenRepository;
        _tripClient = tripClient;
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

        await EnsureActorAuthorizedAsync(parcel.TripId, command, cancellationToken);
        var note = command.Note.Trim();

        if (parcel.Status == ParcelStatus.DELIVERY_CONFIRMED
            && parcel.ConfirmedByUserId == command.ActorUserId
            && string.Equals(parcel.ConfirmNote, note, StringComparison.Ordinal)
            && parcel.ConfirmedAt.HasValue)
        {
            return new ManualConfirmDeliveryResponse(
                parcel.Id,
                parcel.Status.ToString(),
                parcel.ConfirmedAt.Value);
        }

        if (parcel.Status != ParcelStatus.DELIVERED_PENDING_CONFIRM)
            throw new BadRequestException("PARCEL_NOT_PENDING_CONFIRM", $"Parcel is in status '{parcel.Status}' and cannot be confirmed.");

        var now = DateTimeOffset.UtcNow;
        var snapshot = await _parcelRepository.TryManualConfirmDeliveryAsync(
            command.ParcelId,
            command.OperatorId,
            command.ActorUserId,
            note,
            now,
            cancellationToken);

        if (snapshot is null)
        {
            var latest = await _parcelRepository.GetManualConfirmationSnapshotAsync(
                command.ParcelId,
                cancellationToken);

            if (latest is not null
                && latest.Status == ParcelStatus.DELIVERY_CONFIRMED
                && latest.ConfirmedAt.HasValue
                && latest.ConfirmedByUserId == command.ActorUserId
                && string.Equals(latest.ConfirmNote, note, StringComparison.Ordinal))
            {
                return new ManualConfirmDeliveryResponse(
                    latest.ParcelId,
                    latest.Status.ToString(),
                    latest.ConfirmedAt.Value);
            }

            throw new CodedConflictException(
                "RESOURCE_CONFLICT",
                $"Parcel '{command.ParcelId}' status changed concurrently; cannot confirm delivery.");
        }

        await _deliveryTokenRepository.RevokeActiveAsync(
            parcel.Id,
            now,
            cancellationToken);

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
            VietRide.Shared.Kernel.Time.BusinessTime.ToLocalDate(now),
            0, 0, 1, 0, 0, 0, 0,
            cancellationToken);

        return new ManualConfirmDeliveryResponse(snapshot.ParcelId, snapshot.Status.ToString(), now);
    }

    private async Task EnsureActorAuthorizedAsync(
        Guid tripId,
        ManualConfirmDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        var role = command.ActorRole.Trim().ToUpperInvariant();
        if (role is "OPERATOR_ADMIN" or "OPERATOR_STAFF")
        {
            return;
        }

        if (role is not ("DRIVER" or "ASSISTANT"))
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Only operator staff or assigned trip crew can manually confirm delivery.");
        }

        var authorization = await _tripClient.AuthorizeCrewForTripAsync(
            tripId,
            command.ActorUserId,
            command.OperatorId,
            role,
            cancellationToken);

        switch (authorization.Kind)
        {
            case TripCrewAuthorizationOutcomeKind.Authorized:
                return;
            case TripCrewAuthorizationOutcomeKind.Denied:
            case TripCrewAuthorizationOutcomeKind.TripNotFound:
                throw new ForbiddenException(
                    "FORBIDDEN",
                    "Only the Driver or Assistant assigned to this parcel's trip can manually confirm delivery.");
            default:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    authorization.ErrorMessage ?? "Trip service unavailable.");
        }
    }
}
