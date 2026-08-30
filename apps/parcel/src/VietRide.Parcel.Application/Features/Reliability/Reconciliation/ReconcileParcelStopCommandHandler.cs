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

public sealed class ReconcileParcelStopCommandHandler
    : IRequestHandler<ReconcileParcelStopCommand, ReconcileParcelStopResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelStopDepartureApprovalRepository _departureApprovals;
    private readonly ITripServiceClient _trips;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ReconcileParcelStopCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IParcelStopDepartureApprovalRepository departureApprovals,
        ITripServiceClient trips,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _departureApprovals = departureApprovals;
        _trips = trips;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ReconcileParcelStopResponse> Handle(
        ReconcileParcelStopCommand command,
        CancellationToken cancellationToken)
    {
        var authorization = await _trips.AuthorizeAssistantForTripAsync(
            command.TripId,
            command.ActorUserId,
            command.OperatorId,
            cancellationToken);
        if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
            throw new ForbiddenException("FORBIDDEN", "Only the assigned assistant can reconcile this stop.");

        var snapshotOutcome = await _trips.GetTripParcelSnapshotAsync(command.TripId, cancellationToken);
        if (snapshotOutcome.Kind != TripSnapshotOutcomeKind.Success || snapshotOutcome.Snapshot is null)
            throw new CodedConflictException("TRIP_SERVICE_UNAVAILABLE", "Trip operational location is unavailable.");
        if (snapshotOutcome.Snapshot.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Trip does not belong to this operator.");

        var stop = snapshotOutcome.Snapshot.Stops.SingleOrDefault(x => x.StopId == command.StopId)
            ?? throw new CodedNotFoundException("STOP_NOT_FOUND", "Trip stop was not found.");
        var summaryOutcome = await _trips.GetTripSummariesAsync([command.TripId], cancellationToken);
        var stopName = summaryOutcome is { Kind: TripSummaryBatchOutcomeKind.Success }
            ? summaryOutcome.Summaries.FirstOrDefault()?.Stops.FirstOrDefault(item => item.StopId == command.StopId)?.Name
            : null;
        var operationalOutcome = await _trips.GetTripOperationalLocationAsync(
            command.TripId,
            cancellationToken);
        if (operationalOutcome is null
            || operationalOutcome.Kind != TripOperationalLocationOutcomeKind.Success
            || operationalOutcome.Snapshot is null)
            throw new CodedConflictException(
                "TRIP_SERVICE_UNAVAILABLE",
                "Trip operational location is unavailable.");
        if (operationalOutcome.Snapshot.TripId != command.TripId
            || operationalOutcome.Snapshot.CurrentStopId != command.StopId
            || !string.Equals(
                operationalOutcome.Snapshot.CurrentStopStatus,
                "ARRIVED",
                StringComparison.OrdinalIgnoreCase)
            || operationalOutcome.Snapshot.ActualDepartureAt.HasValue)
            throw new CodedConflictException(
                "PARCEL_CUSTODY_LOCATION_MISMATCH",
                "The trip is not currently arrived at this stop or has already departed.",
                [
                    new ValidationError("expectedStop", command.StopId.ToString("D")),
                    new ValidationError(
                        "actualStop",
                        operationalOutcome.Snapshot.CurrentStopId?.ToString("D") ?? string.Empty),
                    new ValidationError("requiredAction", "RECONCILE_CURRENT_OPERATIONAL_STOP"),
                ]);

        var manifest = await _parcels.ListDropoffManifestByTripAndStopAsync(
            command.TripId,
            command.StopId,
            cancellationToken);
        if (manifest.Any(x => x.OperatorId != command.OperatorId))
            throw new ForbiddenException("FORBIDDEN", "Parcel manifest does not belong to this operator.");

        var expected = manifest.ToArray();
        var expectedIds = expected.Select(x => x.Id).ToHashSet();

        var allEvents = await _reliability.ListCustodyEventsByParcelsAsync(expectedIds, cancellationToken);
        var eventsByParcel = allEvents.ToLookup(custodyEvent => custodyEvent.ParcelId);
        var scanned = new HashSet<Guid>();
        var manual = new HashSet<Guid>();
        foreach (var parcel in expected)
        {
            var events = eventsByParcel[parcel.Id];
            if (events.Any(x => x.TripId == command.TripId
                && x.EventType == ParcelCustodyEventType.UNLOADED
                && x.ActualLocationType == ParcelCustodyLocationType.ROUTE_STOP
                && x.ActualLocationId == command.StopId))
                scanned.Add(parcel.Id);
            if (events.Any(x => x.TripId == command.TripId
                && x.EventType == ParcelCustodyEventType.MANUAL_CUSTODY_EXCEPTION
                && x.ActualLocationType == ParcelCustodyLocationType.ROUTE_STOP
                && x.ActualLocationId == command.StopId))
                manual.Add(parcel.Id);
        }

        var unresolved = expectedIds.Except(scanned).Except(manual).ToArray();
        if (unresolved.Length > 0)
        {
            await _departureApprovals.AcquireTripStopLockAsync(
                command.TripId,
                command.StopId,
                cancellationToken);
        }
        var now = _clock.UtcNow;
        var existingIncidents = await _reliability.ListActiveIncidentsByParcelsAsync(unresolved, cancellationToken);
        var incidentByParcel = existingIncidents
            .GroupBy(incident => incident.ParcelId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(incident => incident.CreatedAt).First());
        foreach (var parcel in expected.Where(x => unresolved.Contains(x.Id)))
        {
            await _parcels.TrySetPendingOperatorActionAsync(
                parcel.Id,
                PendingActionType.CUSTODY_EXCEPTION,
                "Parcel was unresolved during stop close reconciliation.",
                null,
                now,
                cancellationToken,
                parcel.Status);

            if (incidentByParcel.TryGetValue(parcel.Id, out var existing))
                continue;

            var current = await _reliability.GetCurrentCustodyAsync(parcel.Id, cancellationToken);
            var leg = await _reliability.GetActiveLegAsync(parcel.Id, cancellationToken);
            var incident = ParcelIncident.Open(
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
                $"STOP:{command.StopId:D}",
                current?.LastLocationSnapshot,
                "Parcel was unresolved during stop close reconciliation.",
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
                    $"STOP:{command.StopId:D}",
                    command.ActorUserId,
                    now.AddMinutes(30)),
                cancellationToken);
            await _reliability.AddSearchTaskAsync(
                ParcelSearchTask.Create(
                    incident.Id,
                    parcel.Id,
                    ParcelSearchTaskType.STATION_INVENTORY,
                    $"STOP:{command.StopId:D}",
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
                    stopId = command.StopId,
                    type = incident.Type.ToString(),
                    source = "STOP_RECONCILIATION",
                    searchDeadline = incident.SearchDeadline,
                },
                cancellationToken);
        }

        var currentCustodies = await _reliability.ListCurrentCustodiesAsync(unresolved, cancellationToken);
        var currentByParcel = currentCustodies.ToDictionary(current => current.ParcelId);
        var unresolvedResponses = expected.Where(parcel => unresolved.Contains(parcel.Id))
            .Select(parcel =>
            {
                currentByParcel.TryGetValue(parcel.Id, out var current);
                incidentByParcel.TryGetValue(parcel.Id, out var incident);
                return new ReconcileUnresolvedParcelResponse(
                    parcel.Id,
                    parcel.ParcelCode,
                    parcel.PhotoUrl,
                    new ReliabilityLocationResponse(
                        "ROUTE_STOP",
                        parcel.DropoffStopId,
                        parcel.DropoffStopId == command.StopId ? stopName : null,
                        stop.OrderIndex,
                        stop.EstimatedArrivalTime),
                    ParcelReliabilityReadModelService.MapCustody(current),
                    incident?.Id,
                    incident?.Type.ToString(),
                    "No verified unload or manual custody event exists for this stop.",
                    "SEARCH_VEHICLE_OR_STATION");
            }).ToArray();

        ParcelStopDepartureApprovalRequest? departureApproval = null;
        var hasAuthorizedOverride = false;
        if (unresolved.Length > 0)
        {
            departureApproval = await _departureApprovals.GetLatestByTripStopForUpdateAsync(
                command.TripId,
                command.StopId,
                cancellationToken);
            hasAuthorizedOverride = departureApproval is not null
                && departureApproval.OperatorId == command.OperatorId
                && departureApproval.Status == ParcelStopDepartureApprovalStatus.APPROVED
                && ParcelStopDepartureApprovalMapper.Matches(departureApproval, unresolved);

            if (!hasAuthorizedOverride && !string.IsNullOrWhiteSpace(command.DepartureOverrideReason))
            {
                var replay = await _departureApprovals.GetByIdempotencyKeyAsync(
                    command.IdempotencyKey,
                    cancellationToken);
                if (replay is not null)
                {
                    if (replay.TripId != command.TripId
                        || replay.StopId != command.StopId
                        || replay.OperatorId != command.OperatorId
                        || !ParcelStopDepartureApprovalMapper.Matches(replay, unresolved))
                        throw new CodedConflictException(
                            "IDEMPOTENCY_KEY_REUSED",
                            "Idempotency-Key was already used for a different departure override request.");
                    departureApproval = replay;
                }
                else if (departureApproval is null
                    || departureApproval.Status != ParcelStopDepartureApprovalStatus.PENDING_APPROVAL
                    || !ParcelStopDepartureApprovalMapper.Matches(departureApproval, unresolved))
                {
                    if (departureApproval?.Status == ParcelStopDepartureApprovalStatus.PENDING_APPROVAL)
                        departureApproval.CancelAsSuperseded(now);
                    departureApproval = ParcelStopDepartureApprovalRequest.Create(
                        command.TripId,
                        command.StopId,
                        command.OperatorId,
                        ParcelStopDepartureApprovalMapper.SerializeParcelIds(unresolved),
                        command.DepartureOverrideReason,
                        command.ActorUserId,
                        "ASSISTANT",
                        now,
                        command.IdempotencyKey);
                    await _departureApprovals.AddAsync(departureApproval, cancellationToken);
                }
            }
        }

        return new ReconcileParcelStopResponse(
            expected.Length,
            expected.Count(x => scanned.Contains(x.Id)),
            expected.Count(x => manual.Contains(x.Id)),
            unresolvedResponses,
            unresolved.Length == 0 || hasAuthorizedOverride,
            unresolved.Length > 0 && !hasAuthorizedOverride,
            departureApproval is null
                ? null
                : ParcelStopDepartureApprovalMapper.Map(departureApproval));
    }
}
