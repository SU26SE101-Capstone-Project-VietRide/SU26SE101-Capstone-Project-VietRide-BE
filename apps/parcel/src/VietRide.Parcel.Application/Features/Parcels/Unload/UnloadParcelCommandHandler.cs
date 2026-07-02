using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Parcel.Application.Features.Parcels.Unload;

public sealed class UnloadParcelCommandHandler
    : IRequestHandler<UnloadParcelCommand, UnloadParcelResponse>
{
    private static readonly TimeSpan DeliveryConfirmWindow = TimeSpan.FromDays(7);

    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public UnloadParcelCommandHandler(
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

    public async Task<UnloadParcelResponse> Handle(
        UnloadParcelCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException(
                "FORBIDDEN",
                $"Operator '{command.OperatorId}' is not assigned to parcel '{command.ParcelId}'.");

        if (parcel.Status != ParcelStatus.IN_TRANSIT)
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel '{command.ParcelId}' is in status '{parcel.Status}' and cannot be unloaded.");

        if (parcel.DropoffStopId.HasValue)
        {
            var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
            switch (tripOutcome.Kind)
            {
                case TripSnapshotOutcomeKind.TripNotFound:
                    throw new CodedNotFoundException(
                        "TRIP_NOT_FOUND",
                        $"Trip with id '{parcel.TripId}' not found.");
                case TripSnapshotOutcomeKind.TransportError:
                    throw new ParcelDependencyUnavailableException(
                        "TRIP_SERVICE_UNAVAILABLE",
                        tripOutcome.ErrorMessage ?? "Trip service unavailable.");
            }

            var trip = tripOutcome.Snapshot!;
            var stop = trip.Stops.FirstOrDefault(s => s.StopId == parcel.DropoffStopId.Value);
            if (stop is null)
                throw new CodedConflictException(
                    "DROP_OFF_STOP_NOT_FOUND",
                    $"Drop-off stop '{parcel.DropoffStopId}' not found in trip '{parcel.TripId}'.");

            if (!stop.AllowDropoff)
                throw new CodedConflictException(
                    "DROP_OFF_STOP_NOT_ALLOWED",
                    $"Drop-off stop '{parcel.DropoffStopId}' does not allow drop-off.");
        }

        var now = DateTimeOffset.UtcNow;
        var deliveryToken = Guid.NewGuid();
        var deliveryTokenExpiresAt = now.Add(DeliveryConfirmWindow);
        ParcelPaymentTransitionSnapshot snapshot;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcelRepository.TryUnloadToPendingConfirmAsync(
                command.ParcelId, deliveryToken, deliveryTokenExpiresAt, now, cancellationToken)
                ?? throw new CodedConflictException(
                    "RACE_LOST",
                    $"Parcel '{command.ParcelId}' status changed concurrently; cannot unload.");

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.Unloaded,
                new { parcelId = snapshot.ParcelId, tripId = snapshot.TripId },
                cancellationToken);
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.DeliveredPendingConfirm,
                BuildDeliveredPendingConfirmPayload(
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

        return new UnloadParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString());
    }

    private static Dictionary<string, object?> BuildDeliveredPendingConfirmPayload(
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
