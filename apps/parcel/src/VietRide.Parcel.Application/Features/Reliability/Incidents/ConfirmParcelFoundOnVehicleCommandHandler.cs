using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class ConfirmParcelFoundOnVehicleCommandHandler
    : IRequestHandler<ConfirmParcelFoundOnVehicleCommand, ConfirmParcelFoundOnVehicleResult>
{
    private const string ResolutionCode = "CREW_CONFIRMED_ON_VEHICLE";

    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly ITripServiceClient _trips;
    private readonly IParcelCustodyService _custody;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ConfirmParcelFoundOnVehicleCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        ITripServiceClient trips,
        IParcelCustodyService custody,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _trips = trips;
        _custody = custody;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ConfirmParcelFoundOnVehicleResult> Handle(
        ConfirmParcelFoundOnVehicleCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");
        if (!string.Equals(parcel.ParcelCode, command.ParcelCode.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new CodedConflictException(
                "SCAN_IDENTITY_MISMATCH",
                "The scanned QR code does not belong to this parcel.",
                [new ValidationError("requiredAction", "VERIFY_PARCEL_IDENTITY")]);

        await EnsureAssignedAssistantAsync(parcel.TripId, command, cancellationToken);

        var incident = await _reliability.GetIncidentAsync(command.IncidentId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Incident was not found.");
        if (incident.ParcelId != parcel.Id || incident.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Incident does not belong to this parcel and operator.");

        var custodyIdempotencyKey = $"assistant-found-on-vehicle:{command.IdempotencyKey:D}";
        var existingEvent = await _reliability.GetCustodyEventByIdempotencyAsync(
            parcel.Id,
            custodyIdempotencyKey,
            cancellationToken);
        if (existingEvent is not null)
        {
            if (existingEvent.EventType != ParcelCustodyEventType.FOUND
                || existingEvent.ActualLocationType != ParcelCustodyLocationType.VEHICLE
                || incident.Status != ParcelIncidentStatus.RESOLVED
                || !string.Equals(incident.ResolutionCode, ResolutionCode, StringComparison.Ordinal))
                throw new CodedConflictException(
                    "IDEMPOTENCY_KEY_REUSED",
                    "Idempotency-Key was already used for a different custody operation.");
            return new ConfirmParcelFoundOnVehicleResult(incident.Id, existingEvent.Id);
        }

        EnsureRecoverableSystemIncident(parcel, incident);
        var operationalLocation = await GetOperationalLocationAsync(parcel.TripId, cancellationToken);
        var now = _clock.UtcNow;

        incident.MarkFound(command.Note);
        incident.Resolve(ResolutionCode, command.Note, now);
        await _reliability.UpdateIncidentAsync(incident, cancellationToken);
        await ParcelIncidentSearchTaskLifecycle.CancelOutstandingAsync(
            _reliability,
            incident.Id,
            now,
            cancellationToken);

        var custodyEvent = await _custody.AppendAsync(
            parcel,
            ParcelCustodyEventType.FOUND,
            ParcelCustodyLocationType.VEHICLE,
            operationalLocation.VehicleId,
            $"VEHICLE:{operationalLocation.VehicleId:D}",
            command.AssistantUserId,
            "ASSISTANT",
            "CREW_FOUND_ON_VEHICLE",
            custodyIdempotencyKey,
            command.EvidenceReferences,
            command.Note,
            cancellationToken);

        var restored = await _parcels.TryResolvePendingOperatorActionAsync(
            parcel.Id,
            PendingActionType.CUSTODY_EXCEPTION,
            now,
            cancellationToken);
        if (restored is null)
            throw new CodedConflictException(
                "INVALID_STATUS",
                "Parcel is no longer waiting for custody incident resolution.");

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.IncidentUpdated,
            new
            {
                incidentId = incident.Id,
                parcelId = parcel.Id,
                operatorId = parcel.OperatorId,
                tripId = parcel.TripId,
                status = incident.Status.ToString(),
                resolutionCode = ResolutionCode,
                actualLocationType = ParcelCustodyLocationType.VEHICLE.ToString(),
                actualLocationId = operationalLocation.VehicleId,
                actorUserId = command.AssistantUserId,
            },
            cancellationToken);

        return new ConfirmParcelFoundOnVehicleResult(incident.Id, custodyEvent.Id);
    }

    private async Task EnsureAssignedAssistantAsync(
        Guid tripId,
        ConfirmParcelFoundOnVehicleCommand command,
        CancellationToken cancellationToken)
    {
        var authorization = await _trips.AuthorizeAssistantForTripAsync(
            tripId,
            command.AssistantUserId,
            command.OperatorId,
            cancellationToken);
        if (authorization.Kind == TripCrewAuthorizationOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                authorization.ErrorMessage ?? "Trip service is unavailable.");
        if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
            throw new ForbiddenException(
                "FORBIDDEN",
                "Only the assigned assistant can confirm this parcel on the vehicle.");
    }

    private async Task<TripOperationalLocationSnapshot> GetOperationalLocationAsync(
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var outcome = await _trips.GetTripOperationalLocationAsync(tripId, cancellationToken);
        if (outcome.Kind == TripOperationalLocationOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip operational location is unavailable.");
        if (outcome.Kind != TripOperationalLocationOutcomeKind.Success || outcome.Snapshot is null)
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        return outcome.Snapshot;
    }

    private static void EnsureRecoverableSystemIncident(
        Domain.Entities.Parcel parcel,
        Domain.Entities.ParcelIncident incident)
    {
        var isSystemMissing = string.Equals(
                incident.ReporterSource,
                "SYSTEM",
                StringComparison.OrdinalIgnoreCase)
            && incident.Type is ParcelIncidentType.MISSING or ParcelIncidentType.MISSING_AFTER_DEPARTURE;
        var isReconciliationGap = incident.Type == ParcelIncidentType.UNSCANNED_HANDOFF
            && incident.ReporterSource is "ASSISTANT" or "SYSTEM";
        if (!isSystemMissing && !isReconciliationGap)
            throw new CodedConflictException(
                "PARCEL_INCIDENT_INVALID_STATUS",
                "Only a system-created missing or reconciliation incident can be resolved by crew vehicle confirmation.");
        if (incident.Status is not (ParcelIncidentStatus.OPEN
            or ParcelIncidentStatus.SEARCHING
            or ParcelIncidentStatus.ESCALATED
            or ParcelIncidentStatus.SEARCH_EXPIRED))
            throw new CodedConflictException(
                "PARCEL_INCIDENT_INVALID_STATUS",
                "Only an active missing incident can be resolved by crew vehicle confirmation.");
        if (parcel.Status != ParcelStatus.PENDING_OPERATOR_ACTION
            || parcel.PendingActionType != PendingActionType.CUSTODY_EXCEPTION
            || parcel.PendingActionResumeStatus is not (ParcelStatus.LOADED or ParcelStatus.IN_TRANSIT))
            throw new CodedConflictException(
                "INVALID_STATUS",
                "Parcel cannot be restored to an on-vehicle transport state.");
    }
}
