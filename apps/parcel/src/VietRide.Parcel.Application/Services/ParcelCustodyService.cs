using System.Text.Json;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Services;

public sealed class ParcelCustodyService : IParcelCustodyService
{
    private readonly IParcelReliabilityRepository _repository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ParcelCustodyService(
        IParcelReliabilityRepository repository,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _repository = repository;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ParcelCustodyEvent> AppendAsync(
        ParcelEntity parcel,
        ParcelCustodyEventType eventType,
        ParcelCustodyLocationType? actualLocationType,
        Guid? actualLocationId,
        string? locationSnapshot,
        Guid? actorId,
        string actorRole,
        string source,
        string? idempotencyKey,
        IReadOnlyCollection<string>? evidenceReferences,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _repository.GetCustodyEventByIdempotencyAsync(
                parcel.Id,
                idempotencyKey,
                cancellationToken);
            if (existing is not null)
                return existing;
        }

        var leg = await _repository.GetActiveLegAsync(parcel.Id, cancellationToken)
            ?? await _repository.GetLatestTransitLegAsync(parcel.Id, cancellationToken);
        var isNewLeg = leg is null;
        if (leg is null)
        {
            leg = ParcelTransitLeg.Create(
                parcel.Id,
                parcel.TripId,
                parcel.OperatorId,
                sequence: 1,
                expectedOriginId: null,
                expectedDestinationId: parcel.DropoffStopId,
                expectedOriginName: parcel.TripSnapshotOriginStationName,
                expectedDestinationName: parcel.DropoffStopId.HasValue
                    ? $"STOP:{parcel.DropoffStopId:D}"
                    : parcel.TripSnapshotDestinationStationName,
                vehicleId: parcel.TripSnapshotVehicleId,
                vehicleLicensePlate: parcel.TripSnapshotVehicleLicensePlate);
        }

        var existingEvents = await _repository.ListCustodyEventsAsync(parcel.Id, cancellationToken);
        var sequence = existingEvents.Count == 0 ? 1 : existingEvents.Max(x => x.Sequence) + 1;
        var now = _clock.UtcNow;
        var previousLegStatus = leg.Status;
        ApplyLegTransition(leg, eventType, actualLocationId, now);
        if (isNewLeg)
            await _repository.AddTransitLegAsync(leg, cancellationToken);
        else if (leg.Status != previousLegStatus)
            await _repository.UpdateTransitLegAsync(leg, cancellationToken);

        var confirmedVehicleId = actualLocationType == ParcelCustodyLocationType.VEHICLE
            && actualLocationId.HasValue
                ? actualLocationId
                : parcel.TripSnapshotVehicleId;
        var custodyEvent = ParcelCustodyEvent.Create(
            parcel.Id,
            leg.Id,
            parcel.TripId,
            eventType,
            parcel.DropoffStopId.HasValue
                ? ParcelCustodyLocationType.ROUTE_STOP
                : ParcelCustodyLocationType.DESTINATION_STATION,
            parcel.DropoffStopId,
            actualLocationType,
            actualLocationId,
            locationSnapshot,
            confirmedVehicleId,
            actorId,
            actorRole,
            now,
            source,
            idempotencyKey,
            evidenceReferences is null ? null : JsonSerializer.Serialize(evidenceReferences),
            reason,
            sequence);

        await _repository.AddCustodyEventAsync(custodyEvent, cancellationToken);
        var current = await _repository.GetCurrentCustodyAsync(parcel.Id, cancellationToken);
        if (current is null)
        {
            await _repository.AddCurrentCustodyAsync(
                ParcelCurrentCustody.Create(parcel.Id, custodyEvent),
                cancellationToken);
        }
        else
        {
            current.Apply(custodyEvent);
            await _repository.UpdateCurrentCustodyAsync(current, cancellationToken);
        }

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            custodyEvent.Id,
            ParcelOutboxEvents.CustodyEventRecorded,
            new
            {
                eventId = custodyEvent.Id,
                occurredAt = custodyEvent.OccurredAt,
                custodyEventId = custodyEvent.Id,
                parcelId = parcel.Id,
                tripId = parcel.TripId,
                operatorId = parcel.OperatorId,
                eventType = custodyEvent.EventType.ToString(),
                actualLocationType = custodyEvent.ActualLocationType?.ToString(),
                actualLocationId = custodyEvent.ActualLocationId,
            },
            cancellationToken);

        return custodyEvent;
    }

    private static void ApplyLegTransition(
        ParcelTransitLeg leg,
        ParcelCustodyEventType eventType,
        Guid? actualLocationId,
        DateTimeOffset occurredAt)
    {
        if (eventType == ParcelCustodyEventType.LOADED
            && leg.Status is ParcelTransitLegStatus.PLANNED or ParcelTransitLegStatus.ACTIVE)
        {
            leg.Start(occurredAt);
            return;
        }

        if (eventType is ParcelCustodyEventType.UNLOADED or ParcelCustodyEventType.DELIVERED
            && leg.Status is ParcelTransitLegStatus.PLANNED or ParcelTransitLegStatus.ACTIVE)
        {
            leg.Complete(actualLocationId, occurredAt);
        }
    }
}
