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

        await EnsureAssignedAssistantAsync(
            parcel.TripId,
            command.ActorUserId,
            command.OperatorId,
            cancellationToken);

        if (parcel.Status != ParcelStatus.IN_TRANSIT)
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel '{command.ParcelId}' is in status '{parcel.Status}' and cannot be unloaded.");

        if (parcel.DropoffStopId.HasValue)
        {
            var trip = await GetRequiredTripSnapshotAsync(parcel.TripId, cancellationToken);
            var stop = trip.Stops.FirstOrDefault(s => s.StopId == parcel.DropoffStopId.Value);
            if (stop is null)
                throw new CodedValidationException(
                    "DROP_OFF_STOP_NOT_FOUND",
                    $"Drop-off stop '{parcel.DropoffStopId}' not found in trip '{parcel.TripId}'.");

            if (!stop.AllowDropoff)
                throw new CodedValidationException(
                    "DROP_OFF_STOP_NOT_ALLOWED",
                    $"Drop-off stop '{parcel.DropoffStopId}' does not allow drop-off.");

            if (!string.Equals(stop.Status, "ARRIVED", StringComparison.OrdinalIgnoreCase))
                throw new CodedValidationException(
                    "DROP_OFF_STOP_NOT_ARRIVED",
                    $"Drop-off stop '{parcel.DropoffStopId}' has not arrived.");
        }
        else
        {
            var trip = await GetRequiredTripSnapshotAsync(parcel.TripId, cancellationToken);
            if (!trip.DestinationArrivedAt.HasValue)
                throw new CodedValidationException(
                    "DESTINATION_TERMINAL_NOT_ARRIVED",
                    $"Trip '{parcel.TripId}' has not arrived at its destination terminal.");
        }

        var now = DateTimeOffset.UtcNow;
        ParcelPaymentTransitionSnapshot snapshot;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = await _parcelRepository.TryMarkUnloadedAsync(
                command.ParcelId, now, cancellationToken)
                ?? throw new CodedConflictException(
                    "INVALID_STATUS",
                    $"Parcel '{command.ParcelId}' status changed concurrently; cannot unload.");

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.Unloaded,
                new { parcelId = snapshot.ParcelId, tripId = snapshot.TripId, userIds = new[] { parcel.SenderUserId }.Concat(parcel.RecipientUserId.HasValue ? new[] { parcel.RecipientUserId.Value } : Array.Empty<Guid>()).Distinct().ToArray() },
                cancellationToken);

            await EnsureCargoSuccessAsync(
                await _tripClient.ReleaseCargoAsync(
                    snapshot.TripId,
                    snapshot.ParcelId,
                    parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
                    parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3,
                    command.IdempotencyKey ?? snapshot.ParcelId,
                    cancellationToken));

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
                    "Only the assigned assistant can unload this parcel.");
            default:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    authorization.ErrorMessage ?? "Trip service unavailable.");
        }
    }

    private async Task<TripParcelSnapshot> GetRequiredTripSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(tripId, cancellationToken);
        switch (tripOutcome.Kind)
        {
            case TripSnapshotOutcomeKind.TripNotFound:
                throw new CodedNotFoundException(
                    "TRIP_NOT_FOUND",
                    $"Trip with id '{tripId}' not found.");
            case TripSnapshotOutcomeKind.TransportError:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    tripOutcome.ErrorMessage ?? "Trip service unavailable.");
        }

        return tripOutcome.Snapshot!;
    }

    private static Task EnsureCargoSuccessAsync(TripCargoOutcome outcome)
    {
        if (outcome is null)
            return Task.CompletedTask;

        return outcome.Kind switch
        {
            TripCargoOutcomeKind.Success => Task.CompletedTask,
            TripCargoOutcomeKind.TripNotFound => throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip was not found."),
            TripCargoOutcomeKind.CapacityExceeded => throw new ParcelDependencyUnavailableException(
                "TRIP_CARGO_CAPACITY_EXCEEDED",
                outcome.ErrorMessage ?? "Trip cargo capacity would be exceeded."),
            _ => throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip service unavailable."),
        };
    }
}
