using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
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
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public DeliverParcelCommandHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
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
        var deliveryToken = Guid.NewGuid();
        var deliveryTokenExpiresAt = now.Add(DeliveryConfirmWindow);
        ParcelPaymentTransitionSnapshot snapshot;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcelRepository.TryMarkDeliveredPendingConfirmAsync(
                command.ParcelId,
                deliveryToken,
                deliveryTokenExpiresAt,
                now,
                cancellationToken)
                ?? throw new CodedConflictException(
                    "INVALID_STATUS",
                    $"Parcel '{command.ParcelId}' status changed concurrently; cannot be delivered.");

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.DeliveredPendingConfirm,
                BuildPayload(
                    snapshot.ParcelId,
                    snapshot.ParcelCode,
                    snapshot.OperatorId,
                    parcel.RecipientUserId,
                    snapshot.TripId,
                    deliveryToken,
                    deliveryTokenExpiresAt),
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
        Guid parcelId,
        string parcelCode,
        Guid operatorId,
        Guid? recipientUserId,
        Guid tripId,
        Guid deliveryToken,
        DateTimeOffset expiresAt)
    {
        var payload = new Dictionary<string, object?>
        {
            ["parcelId"] = parcelId,
            ["parcelCode"] = parcelCode,
            ["operatorId"] = operatorId,
            ["tripId"] = tripId,
            ["deliveryToken"] = deliveryToken,
            ["expiresAt"] = expiresAt,
        };

        if (recipientUserId.HasValue)
        {
            payload["userId"] = recipientUserId.Value;
            payload["recipientUserIds"] = new[] { recipientUserId.Value };
        }

        return payload;
    }
}
