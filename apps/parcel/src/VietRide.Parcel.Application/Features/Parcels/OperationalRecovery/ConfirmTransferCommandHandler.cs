using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed class ConfirmTransferCommandHandler
    : IRequestHandler<ConfirmTransferCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmTransferCommandHandler(
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

        var targetTrip = await _tripClient.GetTripParcelSnapshotAsync(command.TargetTripId, cancellationToken);
        switch (targetTrip.Kind)
        {
            case TripSnapshotOutcomeKind.TripNotFound:
                throw new CodedNotFoundException("TRIP_NOT_FOUND", $"Trip '{command.TargetTripId}' not found.");
            case TripSnapshotOutcomeKind.TransportError:
                throw new ParcelDependencyUnavailableException("TRIP_SERVICE_UNAVAILABLE", targetTrip.ErrorMessage ?? "Trip service unavailable.");
        }

        if (targetTrip.Snapshot!.OperatorId != parcel.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Target trip does not belong to this parcel operator.");

        var now = DateTimeOffset.UtcNow;
        ParcelPaymentTransitionSnapshot snapshot;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcelRepository.TryConfirmTransferAsync(
                command.ParcelId,
                command.TargetTripId,
                command.ParcelCode,
                command.ConfirmedByUserId,
                now,
                cancellationToken)
                ?? throw new CodedConflictException("RACE_LOST", "Parcel status changed concurrently; cannot confirm transfer.");

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.TransferConfirmed,
                new
                {
                    parcelId = snapshot.ParcelId,
                    parcelCode = snapshot.ParcelCode,
                    operatorId = snapshot.OperatorId,
                    userId = snapshot.SenderUserId,
                    tripId = snapshot.TripId,
                    confirmedByUserId = command.ConfirmedByUserId,
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return new OperationalParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            TripId: snapshot.TripId,
            TransferConfirmedAt: now);
    }
}
