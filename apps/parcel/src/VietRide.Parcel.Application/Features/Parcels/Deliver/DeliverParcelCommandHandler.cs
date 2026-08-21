using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Parcel.Application.Features.Parcels.Deliver;

public sealed class DeliverParcelCommandHandler
    : IRequestHandler<DeliverParcelCommand, DeliverParcelResponse>
{
    private static readonly TimeSpan DeliveryConfirmWindow = TimeSpan.FromHours(48);

    private readonly IParcelRepository _parcelRepository;
    private readonly IParcelDeliveryTokenRepository _deliveryTokenRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IParcelDeliveryEmailClient _deliveryEmailClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IParcelCustodyService? _custody;

    public DeliverParcelCommandHandler(
        IParcelRepository parcelRepository,
        IParcelDeliveryTokenRepository deliveryTokenRepository,
        ITripServiceClient tripClient,
        IParcelDeliveryEmailClient deliveryEmailClient,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IParcelCustodyService? custody = null)
    {
        _parcelRepository = parcelRepository;
        _deliveryTokenRepository = deliveryTokenRepository;
        _tripClient = tripClient;
        _deliveryEmailClient = deliveryEmailClient;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _custody = custody;
    }

    public async Task<DeliverParcelResponse> Handle(
        DeliverParcelCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                $"Operator '{command.OperatorId}' is not assigned to parcel '{command.ParcelId}'.");
        }

        await EnsureAssignedAssistantAsync(
            parcel.TripId,
            command.ActorUserId,
            command.OperatorId,
            cancellationToken);

        if (parcel.Status != ParcelStatus.UNLOADED)
        {
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel '{command.ParcelId}' is in status '{parcel.Status}' and cannot be delivered.");
        }

        var now = DateTimeOffset.UtcNow;
        ParcelPaymentTransitionSnapshot snapshot;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcelRepository.TryMarkDeliveredPendingConfirmAsync(
                command.ParcelId,
                ParcelEvidencePhotoRules.Normalize(command.PhotoUrls),
                now,
                cancellationToken)
                ?? throw new CodedConflictException(
                    "INVALID_STATUS",
                    $"Parcel '{command.ParcelId}' status changed concurrently; cannot be delivered.");

            await _deliveryTokenRepository.RevokeActiveAsync(
                parcel.Id,
                now,
                cancellationToken);

            if (_custody is not null)
            {
                await _custody.AppendAsync(
                    parcel,
                    ParcelCustodyEventType.HANDOFF,
                    parcel.DropoffStopId.HasValue
                        ? ParcelCustodyLocationType.ROUTE_STOP
                        : ParcelCustodyLocationType.DESTINATION_STATION,
                    parcel.DropoffStopId,
                    parcel.DropoffStopId.HasValue
                        ? $"STOP:{parcel.DropoffStopId:D}"
                        : parcel.TripSnapshotDestinationStationName,
                    command.ActorUserId,
                    "ASSISTANT",
                    "DELIVERY_HANDOFF",
                    null,
                    command.PhotoUrls,
                    null,
                    cancellationToken);
            }

            DateTimeOffset? expiresAt = null;
            if (!string.IsNullOrWhiteSpace(parcel.RecipientEmail))
            {
                var rawToken = Guid.NewGuid();
                expiresAt = now.Add(DeliveryConfirmWindow);
                var deliveryToken = ParcelDeliveryToken.Issue(
                    parcel.Id,
                    DeliveryTokenHasher.Hash(rawToken),
                    expiresAt.Value,
                    command.ActorUserId,
                    ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY,
                    now);
                await _deliveryTokenRepository.AddAsync(deliveryToken, cancellationToken);

                await _deliveryEmailClient.SendDeliveryLinkAsync(
                    new ParcelDeliveryEmailRequest(
                        deliveryToken.Id,
                        parcel.RecipientEmail,
                        rawToken,
                        parcel.ParcelCode,
                        expiresAt.Value),
                    cancellationToken);
            }

            var eventId = Guid.NewGuid();
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                eventId,
                ParcelOutboxEvents.DeliveredPendingConfirm,
                BuildPayload(
                    eventId,
                    now,
                    snapshot.ParcelId,
                    snapshot.ParcelCode,
                    snapshot.OperatorId,
                    parcel.RecipientUserId,
                    snapshot.TripId,
                    expiresAt),
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return new DeliverParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            now);
    }

    private async Task EnsureAssignedAssistantAsync(
        Guid tripId,
        Guid actorUserId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var authorization = await _tripClient.AuthorizeAssistantForTripAsync(
            tripId,
            actorUserId,
            operatorId,
            cancellationToken);
        switch (authorization.Kind)
        {
            case TripCrewAuthorizationOutcomeKind.Authorized:
                return;
            case TripCrewAuthorizationOutcomeKind.Denied:
            case TripCrewAuthorizationOutcomeKind.TripNotFound:
                throw new ForbiddenException(
                    "FORBIDDEN",
                    "Only the assigned assistant can deliver this parcel.");
            default:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    authorization.ErrorMessage ?? "Trip service unavailable.");
        }
    }

    private static Dictionary<string, object?> BuildPayload(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid parcelId,
        string parcelCode,
        Guid operatorId,
        Guid? recipientUserId,
        Guid tripId,
        DateTimeOffset? expiresAt)
    {
        var payload = new Dictionary<string, object?>
        {
            ["eventId"] = eventId,
            ["occurredAt"] = occurredAt,
            ["parcelId"] = parcelId,
            ["parcelCode"] = parcelCode,
            ["operatorId"] = operatorId,
            ["tripId"] = tripId,
        };

        if (recipientUserId.HasValue)
        {
            payload["userId"] = recipientUserId.Value;
            payload["recipientUserIds"] = new[] { recipientUserId.Value };
        }

        if (expiresAt.HasValue)
        {
            payload["expiresAt"] = expiresAt.Value;
        }

        return payload;
    }
}
