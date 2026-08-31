using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed class ReconcileParcelDestinationCommandHandler
    : IRequestHandler<ReconcileParcelDestinationCommand, ReconcileParcelDestinationResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly ITripServiceClient _trips;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ReconcileParcelDestinationCommandHandler(
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

    public async Task<ReconcileParcelDestinationResponse> Handle(
        ReconcileParcelDestinationCommand command,
        CancellationToken cancellationToken)
    {
        var authorization = await _trips.AuthorizeAssistantForTripAsync(
            command.TripId,
            command.ActorUserId,
            command.OperatorId,
            cancellationToken);
        if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
            throw new ForbiddenException("FORBIDDEN", "Only the assigned assistant can reconcile the destination.");

        var tripOutcome = await _trips.GetTripParcelSnapshotAsync(command.TripId, cancellationToken);
        if (tripOutcome.Kind != TripSnapshotOutcomeKind.Success || tripOutcome.Snapshot is null)
            throw new CodedConflictException("TRIP_SERVICE_UNAVAILABLE", "Trip destination context is unavailable.");
        var trip = tripOutcome.Snapshot;
        if (trip.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Trip does not belong to this operator.");
        if (!trip.DestinationArrivedAt.HasValue)
            throw new CodedConflictException(
                "PARCEL_CUSTODY_LOCATION_MISMATCH",
                "The Trip has not arrived at its destination station.",
                [new ValidationError("requiredAction", "ARRIVE_DESTINATION_BEFORE_RECONCILIATION")]);

        var manifest = await _parcels.ListTerminalDropoffManifestByTripAsync(
            command.TripId,
            cancellationToken);
        if (manifest.Any(parcel => parcel.OperatorId != command.OperatorId))
            throw new ForbiddenException("FORBIDDEN", "Parcel manifest does not belong to this operator.");

        var expectedIds = manifest.Select(parcel => parcel.Id).ToHashSet();
        var events = await _reliability.ListCustodyEventsByParcelsAsync(expectedIds, cancellationToken);
        var byParcel = events.ToLookup(custodyEvent => custodyEvent.ParcelId);
        var scanned = manifest.Where(parcel => byParcel[parcel.Id].Any(custodyEvent =>
                custodyEvent.TripId == command.TripId
                && custodyEvent.EventType == ParcelCustodyEventType.UNLOADED
                && custodyEvent.ActualLocationType == ParcelCustodyLocationType.DESTINATION_STATION
                && custodyEvent.ActualLocationId == trip.DestinationStation.Id))
            .Select(parcel => parcel.Id)
            .ToHashSet();
        var manual = manifest.Where(parcel => byParcel[parcel.Id].Any(custodyEvent =>
                custodyEvent.TripId == command.TripId
                && custodyEvent.EventType == ParcelCustodyEventType.MANUAL_CUSTODY_EXCEPTION
                && custodyEvent.ActualLocationType == ParcelCustodyLocationType.DESTINATION_STATION
                && custodyEvent.ActualLocationId == trip.DestinationStation.Id))
            .Select(parcel => parcel.Id)
            .ToHashSet();
        await ForwardingIncidentResolution.ResolveVerifiedUnloadsAsync(
            scanned,
            _reliability,
            _outbox,
            _clock.UtcNow,
            cancellationToken);
        var unresolved = expectedIds.Except(scanned).Except(manual).ToArray();
        var now = _clock.UtcNow;
        var activeIncidents = await _reliability.ListActiveIncidentsByParcelsAsync(unresolved, cancellationToken);
        var incidentByParcel = activeIncidents
            .GroupBy(incident => incident.ParcelId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(incident => incident.CreatedAt).First());

        foreach (var parcel in manifest.Where(parcel => unresolved.Contains(parcel.Id)))
        {
            if (!incidentByParcel.TryGetValue(parcel.Id, out var incident))
            {
                var current = await _reliability.GetCurrentCustodyAsync(parcel.Id, cancellationToken);
                var leg = await _reliability.GetActiveLegAsync(parcel.Id, cancellationToken);
                incident = ParcelIncident.Open(
                    parcel.Id,
                    parcel.OperatorId,
                    ParcelIncidentType.UNSCANNED_HANDOFF,
                    now.AddHours(parcel.SearchSlaHoursSnapshot > 0
                        ? parcel.SearchSlaHoursSnapshot
                        : ParcelCompensationPolicy.DefaultSearchSlaHours),
                    parcel.TripId,
                    leg?.Id,
                    command.ActorUserId,
                    "ASSISTANT",
                    $"DESTINATION_STATION:{trip.DestinationStation.Id:D}",
                    current?.LastLocationSnapshot,
                    "Parcel was unresolved during destination close reconciliation.",
                    null,
                    operatorProcessBreach: true);
                incident.StartSearch();
                await _reliability.AddIncidentAsync(incident, cancellationToken);
                incidentByParcel[parcel.Id] = incident;
                await _reliability.AddSearchTaskAsync(
                    ParcelSearchTask.Create(
                        incident.Id,
                        parcel.Id,
                        ParcelSearchTaskType.VEHICLE_SWEEP,
                        $"DESTINATION_STATION:{trip.DestinationStation.Id:D}",
                        command.ActorUserId,
                        now.AddMinutes(30)),
                    cancellationToken);
                await _reliability.AddSearchTaskAsync(
                    ParcelSearchTask.Create(
                        incident.Id,
                        parcel.Id,
                        ParcelSearchTaskType.STATION_INVENTORY,
                        $"DESTINATION_STATION:{trip.DestinationStation.Id:D}",
                        null,
                        now.AddHours(2)),
                    cancellationToken);
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    ParcelOutboxEvents.IncidentOpened,
                    new
                    {
                        incidentId = incident.Id,
                        parcelId = parcel.Id,
                        operatorId = parcel.OperatorId,
                        tripId = parcel.TripId,
                        destinationStationId = trip.DestinationStation.Id,
                        type = incident.Type.ToString(),
                        source = "DESTINATION_RECONCILIATION",
                        searchDeadline = incident.SearchDeadline,
                    },
                    cancellationToken);
            }

            if (parcel.Status is ParcelStatus.LOADED or ParcelStatus.IN_TRANSIT)
            {
                var quarantined = await _parcels.TrySetPendingOperatorActionAsync(
                    parcel.Id,
                    PendingActionType.CUSTODY_EXCEPTION,
                    "Parcel was unresolved during destination reconciliation.",
                    null,
                    now,
                    cancellationToken,
                    parcel.Status);
                if (!quarantined)
                    throw new CodedConflictException(
                        "INVALID_STATUS",
                        "Parcel status changed during destination reconciliation.");
            }
        }

        var currentCustodies = await _reliability.ListCurrentCustodiesAsync(unresolved, cancellationToken);
        var currentByParcel = currentCustodies.ToDictionary(current => current.ParcelId);
        var unresolvedResponses = manifest.Where(parcel => unresolved.Contains(parcel.Id))
            .Select(parcel =>
            {
                currentByParcel.TryGetValue(parcel.Id, out var current);
                incidentByParcel.TryGetValue(parcel.Id, out var incident);
                return new ReconcileUnresolvedParcelResponse(
                    parcel.Id,
                    parcel.ParcelCode,
                    parcel.PhotoUrl,
                    new ReliabilityLocationResponse(
                        "DESTINATION_STATION",
                        trip.DestinationStation.Id,
                        trip.DestinationStation.Name,
                        Eta: trip.EstimatedArrivalTime),
                    ParcelReliabilityReadModelService.MapCustody(current),
                    incident?.Id,
                    incident?.Type.ToString(),
                    "No verified unload or manual custody event exists at the destination.",
                    "SEARCH_VEHICLE_OR_DESTINATION_STATION");
            })
            .ToArray();

        var scannedCount = manifest.Count(parcel => scanned.Contains(parcel.Id));
        var completionDecision = ParcelTripCompletionClearancePolicy.Evaluate(
            manifest,
            incidentByParcel.Values.ToArray());
        return new ReconcileParcelDestinationResponse(
            manifest.Count,
            scannedCount,
            manifest.Count(parcel => manual.Contains(parcel.Id)),
            unresolvedResponses,
            CanComplete: completionDecision.CanCompleteTrip,
            CanCompleteTrip: completionDecision.CanCompleteTrip,
            AllExpectedParcelsDelivered: scannedCount == manifest.Count,
            RequiresDriverCompletion: completionDecision.RequiresDriverCompletion);
    }
}
