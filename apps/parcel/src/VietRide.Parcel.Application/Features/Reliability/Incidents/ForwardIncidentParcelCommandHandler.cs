using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class ForwardIncidentParcelCommandHandler
    : IRequestHandler<ForwardIncidentParcelCommand, ParcelIncidentListItem>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly ITripServiceClient _trips;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ForwardIncidentParcelCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        ITripServiceClient trips,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _trips = trips;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ParcelIncidentListItem> Handle(
        ForwardIncidentParcelCommand command,
        CancellationToken cancellationToken)
    {
        var incident = await _reliability.GetIncidentAsync(command.IncidentId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Incident was not found.");
        if (incident.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Incident does not belong to this operator.");
        if (incident.Status != ParcelIncidentStatus.FOUND)
            throw new CodedConflictException(
                "PARCEL_INCIDENT_INVALID_STATUS",
                "Only a found incident can enter forwarding.");
        var parcelEntity = await _parcels.GetByIdAsync(incident.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcelEntity.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        var target = await _trips.GetTripParcelSnapshotAsync(command.TargetTripId, cancellationToken);
        if (target.Kind != TripSnapshotOutcomeKind.Success || target.Snapshot is null)
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Forwarding trip was not found.");
        if (target.Snapshot.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Forwarding trip does not belong to this operator.");
        if (!target.Snapshot.AssistantUserId.HasValue)
            throw new CodedConflictException(
                "PARCEL_ASSISTANT_REQUIRED",
                "The forwarding Trip must have an assigned Assistant.");
        if (string.Equals(target.Snapshot.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target.Snapshot.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            throw new CodedConflictException("INVALID_STATUS", "Forwarding trip is not operational.");

        var now = _clock.UtcNow;
        var parcel = await _parcels.TryRequestReliabilityForwardingAsync(
            incident.ParcelId,
            command.OperatorId,
            command.TargetTripId,
            now,
            cancellationToken)
            ?? throw new CodedConflictException(
                "INVALID_STATUS",
                "Parcel cannot enter the forwarding confirmation flow from its current state.");

        var forwardingLeg = await _reliability.GetTransitLegAsync(
            parcel.ParcelId,
            command.TargetTripId,
            cancellationToken);
        if (forwardingLeg is null)
        {
            var latestLeg = await _reliability.GetLatestTransitLegAsync(parcel.ParcelId, cancellationToken);
            var current = await _reliability.GetCurrentCustodyAsync(parcel.ParcelId, cancellationToken);
            forwardingLeg = ParcelTransitLeg.Create(
                parcel.ParcelId,
                command.TargetTripId,
                parcel.OperatorId,
                (latestLeg?.Sequence ?? 0) + 1,
                current?.LastLocationId,
                parcelEntity.DropoffStopId ?? target.Snapshot.DestinationStation.Id,
                current?.LastLocationSnapshot,
                parcelEntity.DropoffStopId.HasValue
                    ? $"STOP:{parcelEntity.DropoffStopId:D}"
                    : target.Snapshot.DestinationStation.Name,
                target.Snapshot.VehicleId,
                null);
            await _reliability.AddTransitLegAsync(forwardingLeg, cancellationToken);
        }

        incident.StartForwarding();
        await _reliability.UpdateIncidentAsync(incident, cancellationToken);
        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.IncidentUpdated,
            new
            {
                incidentId = incident.Id,
                parcelId = incident.ParcelId,
                operatorId = incident.OperatorId,
                sourceTripId = parcel.TripId,
                targetTripId = command.TargetTripId,
                actorUserId = command.ActorUserId,
                status = incident.Status.ToString(),
            },
            cancellationToken);

        return new ParcelIncidentListItem(
            incident.Id,
            incident.ParcelId,
            incident.OperatorId,
            incident.Type.ToString(),
            incident.Status.ToString(),
            incident.TripId,
            incident.LastKnownLocation,
            incident.SearchDeadline,
            incident.CreatedAt,
            incident.OperatorProcessBreach);
    }
}
