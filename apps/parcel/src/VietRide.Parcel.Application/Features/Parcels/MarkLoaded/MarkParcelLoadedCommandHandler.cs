using System.Text.Json;
using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.MarkLoaded;

public sealed class MarkParcelLoadedCommandHandler
    : IRequestHandler<MarkParcelLoadedCommand, MarkParcelLoadedResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public MarkParcelLoadedCommandHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _outbox = outbox;
        _statsRepository = statsRepository;
    }

    public async Task<MarkParcelLoadedResponse> Handle(
        MarkParcelLoadedCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{command.ParcelId}' not found.");

        if (command.OperatorId.HasValue && parcel.OperatorId != command.OperatorId.Value)
            throw new ForbiddenException(
                "FORBIDDEN",
                $"Operator '{command.OperatorId}' is not assigned to parcel '{command.ParcelId}'.");

        if (parcel.TripId != command.TripId)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{command.ParcelId}' not found.");

        if (command.OperatorId.HasValue && command.LoadedByUserId.HasValue)
        {
            await EnsureAssignedAssistantAsync(
                parcel.TripId,
                command.LoadedByUserId.Value,
                command.OperatorId.Value,
                cancellationToken);
        }

        if (parcel.ParcelCode != command.ParcelCode)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{command.ParcelId}' not found.");

        if (parcel.Status != ParcelStatus.PENDING)
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel '{command.ParcelId}' is in status '{parcel.Status}' and cannot be loaded.");

        var now = DateTimeOffset.UtcNow;
        var snapshot = await _parcelRepository.TryMarkLoadedAsync(
            command.ParcelId,
            command.TripId,
            command.ParcelCode,
            command.LoadedByUserId,
            now,
            cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel '{command.ParcelId}' status changed concurrently; cannot mark loaded.");

        await EnsureCargoSuccessAsync(
            await _tripClient.LoadCargoAsync(
                snapshot.TripId,
                snapshot.ParcelId,
                parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
                parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3,
                command.IdempotencyKey ?? snapshot.ParcelId,
                cancellationToken));

        var eventId = Guid.NewGuid();
        var userIds = new[] { parcel.SenderUserId }
            .Concat(parcel.RecipientUserId.HasValue
                ? new[] { parcel.RecipientUserId.Value }
                : Array.Empty<Guid>())
            .Distinct()
            .ToArray();
        var payload = JsonSerializer.Serialize(
            new
            {
                eventId,
                occurredAt = now,
                parcelId = snapshot.ParcelId,
                tripId = snapshot.TripId,
                actualWeightKg = parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
                userIds,
            },
            JsonOptions);
        await _outbox.EnqueueAsync(
            eventId,
            ParcelOutboxEvents.Loaded,
            payload,
            cancellationToken);

        await _statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0, 1, 0, 0, 0, 0, 0,
            cancellationToken);

        return new MarkParcelLoadedResponse(snapshot.ParcelId, snapshot.ParcelCode, snapshot.Status.ToString());
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
                    "Only the assigned assistant can load this parcel.");
            default:
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    authorization.ErrorMessage ?? "Trip service unavailable.");
        }
    }

    private static Task EnsureCargoSuccessAsync(TripCargoOutcome outcome)
    {
        if (outcome is null)
            return Task.CompletedTask;

        return outcome.Kind switch
        {
            TripCargoOutcomeKind.Success => Task.CompletedTask,
            TripCargoOutcomeKind.TripNotFound => throw new ParcelDependencyUnavailableException(
                "TRIP_NOT_FOUND",
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
