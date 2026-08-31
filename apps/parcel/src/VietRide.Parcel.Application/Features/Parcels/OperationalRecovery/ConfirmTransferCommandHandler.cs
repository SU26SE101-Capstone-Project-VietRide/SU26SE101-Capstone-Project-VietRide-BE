using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed class ConfirmTransferCommandHandler
    : IRequestHandler<ConfirmTransferCommand, OperationalParcelResponse>
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromMinutes(30);

    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IParcelReliabilityRepository? _reliability;

    public ConfirmTransferCommandHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        IParcelReliabilityRepository? reliability = null)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _reliability = reliability;
    }

    public async Task<OperationalParcelResponse> Handle(
        ConfirmTransferCommand command,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetRequiredSnapshotAsync(command.ParcelId, cancellationToken);
        ValidateRequest(snapshot, command);
        await AuthorizeCrewAsync(snapshot, command, cancellationToken);

        if (IsCompletedReplay(snapshot, command.IdempotencyKey))
        {
            await EnsureCompletedForwardingCustodyAsync(snapshot, cancellationToken);
            return ToResponse(snapshot);
        }

        var claimed = await AcquireClaimAsync(snapshot, command, cancellationToken);
        var claimId = claimed.ClaimId!.Value;
        var targetTripId = claimed.TargetTripId!.Value;
        var confirmedByUserId = claimed.ClaimedByUserId!.Value;
        var plannedReliabilityLeg = _reliability is null
            ? null
            : await _reliability.GetTransitLegAsync(claimed.ParcelId, targetTripId, cancellationToken);

        var transfer = await _tripClient.TransferCargoAsync(
            claimed.SourceTripId,
            claimed.ParcelId,
            targetTripId,
            "LOADED",
            allowCapacityOverflow: plannedReliabilityLeg is null,
            claimId,
            cancellationToken);

        if (transfer.Kind == TripCargoTransferOutcomeKind.Success)
        {
            Guid? targetVehicleId = null;
            if (_reliability is not null)
            {
                var targetSnapshot = await _tripClient.GetTripParcelSnapshotAsync(targetTripId, cancellationToken);
                if (targetSnapshot.Kind != TripSnapshotOutcomeKind.Success || targetSnapshot.Snapshot is null)
                {
                    throw new ParcelDependencyUnavailableException(
                        "TRIP_SERVICE_UNAVAILABLE",
                        targetSnapshot.ErrorMessage ?? "Target Trip context is unavailable after cargo transfer.");
                }
                targetVehicleId = targetSnapshot.Snapshot.VehicleId;
            }

            var completed = await CompleteAsync(
                claimed,
                targetTripId,
                claimId,
                confirmedByUserId,
                targetVehicleId,
                cancellationToken);
            return ToResponse(completed);
        }

        if (transfer.Kind is TripCargoTransferOutcomeKind.TripNotFound
            or TripCargoTransferOutcomeKind.ParcelCargoNotFound
            or TripCargoTransferOutcomeKind.Conflict
            or TripCargoTransferOutcomeKind.CapacityExceeded)
        {
            return await ClearClaimAndThrowAsync(
                claimed.ParcelId,
                claimId,
                transfer,
                cancellationToken);
        }

        throw new ParcelDependencyUnavailableException(
            "TRIP_SERVICE_UNAVAILABLE",
            transfer.ErrorMessage ?? "Trip cargo transfer outcome is unknown.");
    }

    private async Task EnsureCompletedForwardingCustodyAsync(
        ParcelTransferConfirmationSnapshot completed,
        CancellationToken cancellationToken)
    {
        if (_reliability is null
            || completed.TargetTripId is not Guid targetTripId
            || completed.ClaimId is not Guid operationId
            || completed.TransferConfirmedByUserId is not Guid actorUserId)
        {
            return;
        }

        var targetSnapshot = await _tripClient.GetTripParcelSnapshotAsync(targetTripId, cancellationToken);
        if (targetSnapshot.Kind != TripSnapshotOutcomeKind.Success || targetSnapshot.Snapshot is null)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                targetSnapshot.ErrorMessage ?? "Target Trip context is unavailable while repairing forwarding custody.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await RecordForwardingCustodyAsync(
                completed.ParcelId,
                completed.SourceTripId,
                targetTripId,
                operationId,
                actorUserId,
                targetSnapshot.Snapshot.VehicleId,
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task RecordForwardingCustodyAsync(
        Guid parcelId,
        Guid sourceTripId,
        Guid targetTripId,
        Guid operationId,
        Guid actorUserId,
        Guid? targetVehicleId,
        CancellationToken cancellationToken)
    {
        if (_reliability is null)
            return;

        var parcel = await _parcelRepository.GetByIdAsync(parcelId, cancellationToken);
        if (parcel is null)
            return;

        var oldLeg = await _reliability.GetTransitLegAsync(parcel.Id, sourceTripId, cancellationToken);
        var targetLeg = await _reliability.GetTransitLegAsync(parcel.Id, targetTripId, cancellationToken);
        var existingEvents = await _reliability.ListCustodyEventsAsync(parcel.Id, cancellationToken);
        var sequence = existingEvents.Count == 0 ? 1 : existingEvents.Max(x => x.Sequence) + 1;
        var current = await _reliability.GetCurrentCustodyAsync(parcel.Id, cancellationToken);
        var now = _clock.UtcNow;
        var forwardedOutKey = $"forward:{operationId:D}:out";
        var forwardedInKey = $"forward:{operationId:D}:in";
        var existingForwardedOut = existingEvents.FirstOrDefault(x => x.IdempotencyKey == forwardedOutKey);
        var existingForwardedIn = existingEvents.FirstOrDefault(x => x.IdempotencyKey == forwardedInKey);

        if (oldLeg is not null)
        {
            if (oldLeg.Status != ParcelTransitLegStatus.FORWARDED)
            {
                oldLeg.MarkForwarded(now);
                await _reliability.UpdateTransitLegAsync(oldLeg, cancellationToken);
            }
            if (existingForwardedOut is null)
            {
                var forwardedOut = ParcelCustodyEvent.Create(
                    parcel.Id,
                    oldLeg.Id,
                    sourceTripId,
                    ParcelCustodyEventType.FORWARDED_OUT,
                    parcel.DropoffStopId.HasValue ? ParcelCustodyLocationType.ROUTE_STOP : ParcelCustodyLocationType.DESTINATION_STATION,
                    parcel.DropoffStopId,
                    current?.LastLocationType,
                    current?.LastLocationId,
                    current?.LastLocationSnapshot,
                    current?.CurrentVehicleId,
                    actorUserId,
                    "ASSISTANT",
                    now,
                    "TRANSFER_CONFIRMATION",
                    forwardedOutKey,
                    null,
                    null,
                    sequence++);
                await _reliability.AddCustodyEventAsync(forwardedOut, cancellationToken);
                await EnqueueCustodyEventAsync(forwardedOut, parcel, cancellationToken);
            }
        }

        var newLeg = targetLeg ?? ParcelTransitLeg.Create(
            parcel.Id,
            targetTripId,
            parcel.OperatorId,
            (oldLeg?.Sequence ?? 0) + 1,
            current?.LastLocationId,
            parcel.DropoffStopId,
            current?.LastLocationSnapshot,
            parcel.DropoffStopId.HasValue
                ? $"STOP:{parcel.DropoffStopId:D}"
                : parcel.TripSnapshotDestinationStationName,
            targetVehicleId,
            null);
        if (newLeg.Status is ParcelTransitLegStatus.PLANNED or ParcelTransitLegStatus.ACTIVE)
            newLeg.Start(now);
        if (targetLeg is null)
            await _reliability.AddTransitLegAsync(newLeg, cancellationToken);
        else
            await _reliability.UpdateTransitLegAsync(newLeg, cancellationToken);

        if (existingForwardedIn is null)
        {
            var forwardedIn = ParcelCustodyEvent.Create(
                parcel.Id,
                newLeg.Id,
                targetTripId,
                ParcelCustodyEventType.FORWARDED_IN,
                parcel.DropoffStopId.HasValue ? ParcelCustodyLocationType.ROUTE_STOP : ParcelCustodyLocationType.DESTINATION_STATION,
                parcel.DropoffStopId,
                ParcelCustodyLocationType.VEHICLE,
                targetVehicleId,
                targetVehicleId.HasValue ? $"VEHICLE:{targetVehicleId:D}" : $"TRIP:{targetTripId:D}",
                targetVehicleId,
                actorUserId,
                "ASSISTANT",
                now,
                "TRANSFER_CONFIRMATION",
                forwardedInKey,
                null,
                null,
                sequence);
            await _reliability.AddCustodyEventAsync(forwardedIn, cancellationToken);
            await EnqueueCustodyEventAsync(forwardedIn, parcel, cancellationToken);
            if (current is null)
                await _reliability.AddCurrentCustodyAsync(ParcelCurrentCustody.Create(parcel.Id, forwardedIn), cancellationToken);
            else
            {
                current.Apply(forwardedIn);
                await _reliability.UpdateCurrentCustodyAsync(current, cancellationToken);
            }
        }
        else if (current is null)
        {
            await _reliability.AddCurrentCustodyAsync(
                ParcelCurrentCustody.Create(parcel.Id, existingForwardedIn),
                cancellationToken);
        }
        else if (current.LastSequence < existingForwardedIn.Sequence)
        {
            current.Apply(existingForwardedIn);
            await _reliability.UpdateCurrentCustodyAsync(current, cancellationToken);
        }
    }

    private Task EnqueueCustodyEventAsync(
        ParcelCustodyEvent custodyEvent,
        VietRide.Parcel.Domain.Entities.Parcel parcel,
        CancellationToken cancellationToken)
        => ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            custodyEvent.Id,
            ParcelOutboxEvents.CustodyEventRecorded,
            new
            {
                eventId = custodyEvent.Id,
                occurredAt = custodyEvent.OccurredAt,
                custodyEventId = custodyEvent.Id,
                parcelId = parcel.Id,
                tripId = custodyEvent.TripId,
                operatorId = parcel.OperatorId,
                eventType = custodyEvent.EventType.ToString(),
                actualLocationType = custodyEvent.ActualLocationType?.ToString(),
                actualLocationId = custodyEvent.ActualLocationId,
            },
            cancellationToken);


    private async Task<ParcelTransferConfirmationSnapshot> AcquireClaimAsync(
        ParcelTransferConfirmationSnapshot initial,
        ConfirmTransferCommand command,
        CancellationToken cancellationToken)
    {
        if (initial.ClaimId is not null)
        {
            EnsurePersistedClaimIsComplete(initial);
            return initial;
        }

        var now = _clock.UtcNow;
        if (HasReachedDeadline(initial, now))
        {
            throw DeadlinePassed();
        }

        ParcelTransferConfirmationSnapshot? claimed;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            claimed = await _parcelRepository.TryClaimTransferConfirmationAsync(
                initial.ParcelId,
                initial.ParcelCode,
                initial.SourceTripId,
                initial.TargetTripId!.Value,
                command.IdempotencyKey,
                command.ConfirmedByUserId,
                now,
                cancellationToken);

            if (claimed is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
            }
            else
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (claimed is not null)
        {
            EnsurePersistedClaimIsComplete(claimed);
            return claimed;
        }

        var concurrent = await GetRequiredSnapshotAsync(command.ParcelId, cancellationToken);
        ValidateRequest(concurrent, command);
        if (IsCompletedReplay(concurrent, command.IdempotencyKey))
        {
            return concurrent;
        }

        if (concurrent.SourceTripId != initial.SourceTripId
            || concurrent.TargetTripId != initial.TargetTripId)
        {
            throw NotTransferable("Parcel transfer target changed concurrently.");
        }

        if (concurrent.ClaimId is not null)
        {
            EnsurePersistedClaimIsComplete(concurrent);
            return concurrent;
        }

        if (HasReachedDeadline(concurrent, _clock.UtcNow))
        {
            throw DeadlinePassed();
        }

        throw NotTransferable("Parcel transfer confirmation lost a concurrent state change.");
    }

    private async Task<ParcelTransferConfirmationSnapshot> CompleteAsync(
        ParcelTransferConfirmationSnapshot claimed,
        Guid targetTripId,
        Guid claimId,
        Guid confirmedByUserId,
        Guid? targetVehicleId,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        ParcelTransferConfirmationSnapshot? completed;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            completed = await _parcelRepository.TryCompleteTransferConfirmationAsync(
                claimed.ParcelId,
                claimed.SourceTripId,
                targetTripId,
                claimId,
                confirmedByUserId,
                now,
                cancellationToken);

            if (completed is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
            }
            else
            {
                await RecordForwardingCustodyAsync(
                    claimed.ParcelId,
                    claimed.SourceTripId,
                    targetTripId,
                    claimId,
                    confirmedByUserId,
                    targetVehicleId,
                    cancellationToken);

                var eventId = ParcelOperationId.Create(
                    claimId,
                    claimed.ParcelId,
                    "TRANSFER_CONFIRMED_EVENT");
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    eventId,
                    ParcelOutboxEvents.TransferConfirmed,
                    new
                    {
                        eventId,
                        occurredAt = now,
                        parcelId = completed.ParcelId,
                        parcelCode = completed.ParcelCode,
                        operatorId = completed.OperatorId,
                        userId = completed.SenderUserId,
                        originalTripId = claimed.SourceTripId,
                        tripId = targetTripId,
                        confirmedByUserId,
                    },
                    cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (completed is not null)
        {
            return completed;
        }

        var replay = await GetRequiredSnapshotAsync(claimed.ParcelId, cancellationToken);
        if (IsCompletedReplay(replay, claimId)
            && replay.TargetTripId == targetTripId)
        {
            return replay;
        }

        throw NotTransferable("Parcel transfer completion lost a concurrent state change.");
    }

    private async Task<OperationalParcelResponse> ClearClaimAndThrowAsync(
        Guid parcelId,
        Guid claimId,
        TripCargoTransferOutcome outcome,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var cleared = await _parcelRepository.TryClearTransferConfirmationClaimAsync(
                parcelId,
                claimId,
                _clock.UtcNow,
                cancellationToken);
            if (cleared)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
            }
            else
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
            }
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        throw outcome.Kind switch
        {
            TripCargoTransferOutcomeKind.TripNotFound => new CodedNotFoundException(
                "TRIP_NOT_FOUND",
                outcome.ErrorMessage ?? "The source or target Trip was not found."),
            TripCargoTransferOutcomeKind.ParcelCargoNotFound => new CodedNotFoundException(
                "PARCEL_CARGO_NOT_FOUND",
                outcome.ErrorMessage ?? "The source Trip has no active cargo ledger for this Parcel."),
            TripCargoTransferOutcomeKind.Conflict => new CodedConflictException(
                "TRIP_CARGO_TRANSFER_CONFLICT",
                outcome.ErrorMessage ?? "Trip cargo transfer lost a concurrent mutation."),
            TripCargoTransferOutcomeKind.CapacityExceeded => new CodedValidationException(
                "TRIP_CARGO_CAPACITY_EXCEEDED",
                outcome.ErrorMessage ?? "Target Trip cargo capacity would be exceeded."),
            _ => throw new InvalidOperationException("Only definitive Trip outcomes may clear a transfer claim."),
        };
    }

    private async Task AuthorizeCrewAsync(
        ParcelTransferConfirmationSnapshot snapshot,
        ConfirmTransferCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.RequireCrewAuthorization)
        {
            return;
        }

        if (command.OperatorId is null
            || snapshot.OperatorId != command.OperatorId
            || command.Role is null)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Caller is not scoped to the Parcel operator.");
        }

        var authorization = await _tripClient.AuthorizeCrewForTripAsync(
            snapshot.TargetTripId!.Value,
            command.ConfirmedByUserId,
            command.OperatorId.Value,
            command.Role,
            cancellationToken);

        switch (authorization.Kind)
        {
            case TripCrewAuthorizationOutcomeKind.Authorized:
                return;
            case TripCrewAuthorizationOutcomeKind.Denied:
            case TripCrewAuthorizationOutcomeKind.TripNotFound:
                throw new ForbiddenException(
                    "FORBIDDEN",
                    "Caller is not assigned to the target Trip as Driver or Assistant.");
            default:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    authorization.ErrorMessage ?? "Trip crew authorization is unavailable.");
        }
    }

    private async Task<ParcelTransferConfirmationSnapshot> GetRequiredSnapshotAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
        => await _parcelRepository.GetTransferConfirmationSnapshotAsync(
            parcelId,
            cancellationToken)
            ?? throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel '{parcelId}' not found.");

    private static void ValidateRequest(
        ParcelTransferConfirmationSnapshot snapshot,
        ConfirmTransferCommand command)
    {
        if (!string.Equals(snapshot.ParcelCode, command.ParcelCode, StringComparison.Ordinal)
            || snapshot.TargetTripId is null
            || (command.ExpectedTargetTripId is not null
                && snapshot.TargetTripId != command.ExpectedTargetTripId))
        {
            throw NotTransferable("Parcel code or transfer target does not match the pending transfer.");
        }

        if (snapshot.Status == ParcelStatus.TRANSFER_ESCALATED
            && snapshot.ClaimId is null)
        {
            throw DeadlinePassed();
        }

        if (snapshot.Status != ParcelStatus.PENDING_TRANSFER_CONFIRM
            && !IsCompletedReplay(snapshot, command.IdempotencyKey))
        {
            throw NotTransferable(
                $"Parcel status '{snapshot.Status}' cannot confirm transfer.");
        }

        if (snapshot.Status == ParcelStatus.PENDING_TRANSFER_CONFIRM
            && snapshot.TransferRequestedAt is null)
        {
            throw NotTransferable("Parcel transfer request timestamp is missing.");
        }
    }

    private static void EnsurePersistedClaimIsComplete(
        ParcelTransferConfirmationSnapshot snapshot)
    {
        if (snapshot.ClaimId is null
            || snapshot.ClaimedAt is null
            || snapshot.ClaimedByUserId is null
            || snapshot.TargetTripId is null)
        {
            throw NotTransferable("Parcel transfer confirmation claim is incomplete.");
        }
    }

    private static bool HasReachedDeadline(
        ParcelTransferConfirmationSnapshot snapshot,
        DateTimeOffset now)
        => snapshot.TransferRequestedAt is null
            || now >= snapshot.TransferRequestedAt.Value.Add(ConfirmationWindow);

    private static bool IsCompletedReplay(
        ParcelTransferConfirmationSnapshot snapshot,
        Guid claimId)
        => snapshot.Status == ParcelStatus.LOADED
            && snapshot.ClaimId == claimId
            && snapshot.TargetTripId is not null
            && snapshot.SourceTripId == snapshot.TargetTripId
            && snapshot.TransferConfirmedAt is not null
            && snapshot.TransferConfirmedByUserId is not null;

    private static OperationalParcelResponse ToResponse(
        ParcelTransferConfirmationSnapshot snapshot)
        => new(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            TripId: snapshot.SourceTripId,
            TransferTargetTripId: snapshot.TargetTripId,
            TransferConfirmedAt: snapshot.TransferConfirmedAt);

    private static CodedConflictException NotTransferable(string message)
        => new("PARCEL_NOT_TRANSFERABLE", message);

    private static CodedConflictException DeadlinePassed()
        => new(
            "PARCEL_TRANSFER_CONFIRMATION_DEADLINE_PASSED",
            "The 30-minute transfer confirmation deadline has passed.");
}
