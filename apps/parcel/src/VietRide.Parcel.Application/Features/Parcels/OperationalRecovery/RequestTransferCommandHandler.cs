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

public sealed class RequestTransferCommandHandler
    : IRequestHandler<RequestTransferCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public RequestTransferCommandHandler(
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
        RequestTransferCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        if (parcel.Status is not (ParcelStatus.LOADED or ParcelStatus.IN_TRANSIT))
            throw new CodedConflictException("INVALID_TRANSITION", $"Parcel status '{parcel.Status}' cannot request transfer.");

        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new CodedValidationException("VALIDATION_ERROR", "Transfer reason is required.");

        if (parcel.TripId == command.TargetTripId)
            throw new CodedConflictException("INVALID_TRANSFER_TARGET", "Target trip must differ from current trip.");

        var targetTrip = await _tripClient.GetTripParcelSnapshotAsync(command.TargetTripId, cancellationToken);
        switch (targetTrip.Kind)
        {
            case TripSnapshotOutcomeKind.TripNotFound:
                throw new CodedNotFoundException("TRIP_NOT_FOUND", $"Trip '{command.TargetTripId}' not found.");
            case TripSnapshotOutcomeKind.TransportError:
                throw new ParcelDependencyUnavailableException("TRIP_SERVICE_UNAVAILABLE", targetTrip.ErrorMessage ?? "Trip service unavailable.");
        }

        if (targetTrip.Snapshot!.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Target trip does not belong to this operator.");

        var now = DateTimeOffset.UtcNow;
        ParcelPaymentTransitionSnapshot snapshot;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcelRepository.TryRequestTransferAsync(
                command.ParcelId,
                command.OperatorId,
                command.TargetTripId,
                now,
                cancellationToken)
                ?? throw new CodedConflictException("RACE_LOST", "Parcel status changed concurrently; cannot request transfer.");

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.TransferInitiated,
                new { parcelId = snapshot.ParcelId, originalTripId = parcel.TripId, newTripId = command.TargetTripId, reason = command.Reason.Trim() },
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
            TripId: parcel.TripId,
            TransferTargetTripId: command.TargetTripId);
    }
}
