using System.Text.Json;
using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed class ReportCustodyExceptionCommandHandler
    : IRequestHandler<ReportCustodyExceptionCommand, ReportCustodyExceptionResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelCustodyService _custody;
    private readonly ITripServiceClient _trips;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ReportCustodyExceptionCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IParcelCustodyService custody,
        ITripServiceClient trips,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _custody = custody;
        _trips = trips;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ReportCustodyExceptionResponse> Handle(
        ReportCustodyExceptionCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        if (string.Equals(command.ActorRole, "ASSISTANT", StringComparison.OrdinalIgnoreCase))
        {
            var authorization = await _trips.AuthorizeAssistantForTripAsync(
                parcel.TripId,
                command.ActorUserId,
                command.OperatorId,
                cancellationToken);
            if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
                throw new ForbiddenException("FORBIDDEN", "Only the assigned assistant can report this exception.");
        }

        if (!Enum.TryParse<ParcelIncidentType>(command.IncidentType, true, out var incidentType)
            || !Enum.TryParse<ParcelCustodyLocationType>(command.ActualLocationType, true, out var locationType))
            throw new CodedValidationException("VALIDATION_ERROR", "Incident or location type is invalid.");

        var existingIncident = await _reliability.GetOpenIncidentAsync(
            parcel.Id,
            incidentType,
            cancellationToken);
        if (existingIncident is not null)
            throw new CodedConflictException("PARCEL_INCIDENT_ALREADY_OPEN", "An open incident already exists for this Parcel.");

        var now = _clock.UtcNow;
        var custodyEvent = await _custody.AppendAsync(
            parcel,
            ParcelCustodyEventType.MANUAL_CUSTODY_EXCEPTION,
            locationType,
            command.ActualLocationId,
            command.LocationSnapshot,
            command.ActorUserId,
            command.ActorRole,
            "CUSTODY_EXCEPTION",
            command.IdempotencyKey?.ToString("D"),
            command.EvidenceUrls,
            command.Reason,
            cancellationToken);

        var incident = ParcelIncident.Open(
            parcel.Id,
            parcel.OperatorId,
            incidentType,
            now.AddHours(parcel.SearchSlaHoursSnapshot > 0
                ? parcel.SearchSlaHoursSnapshot
                : ParcelCompensationPolicy.DefaultSearchSlaHours),
            parcel.TripId,
            custodyEvent.LegId,
            command.ActorUserId,
            command.ActorRole,
            parcel.DropoffStopId.HasValue ? $"STOP:{parcel.DropoffStopId:D}" : parcel.TripSnapshotDestinationStationName,
            command.LocationSnapshot,
            command.Description,
            JsonSerializer.Serialize(new
            {
                command.TemporaryExceptionTag,
                command.ObservedWeightKg,
                command.EvidenceUrls,
            }),
            operatorProcessBreach: true);
        incident.StartSearch();
        await _reliability.AddIncidentAsync(incident, cancellationToken);

        await _reliability.AddSearchTaskAsync(
            ParcelSearchTask.Create(
                incident.Id,
                parcel.Id,
                ParcelSearchTaskType.MANIFEST_RECONCILIATION,
                command.LocationSnapshot,
                null,
                now.AddMinutes(30)),
            cancellationToken);
        await _reliability.AddSearchTaskAsync(
            ParcelSearchTask.Create(
                incident.Id,
                parcel.Id,
                ParcelSearchTaskType.VEHICLE_SWEEP,
                command.LocationSnapshot,
                null,
                now.AddHours(2)),
            cancellationToken);

        await _parcels.TrySetPendingOperatorActionAsync(
            parcel.Id,
            PendingActionType.CUSTODY_EXCEPTION,
            command.Reason,
            null,
            now,
            cancellationToken,
            parcel.Status);

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.IncidentOpened,
            new
            {
                incidentId = incident.Id,
                parcelId = parcel.Id,
                operatorId = parcel.OperatorId,
                type = incident.Type.ToString(),
                searchDeadline = incident.SearchDeadline,
                lastKnownLocation = command.LocationSnapshot,
            },
            cancellationToken);

        return new ReportCustodyExceptionResponse(
            parcel.Id,
            incident.Id,
            incident.Type.ToString(),
            incident.Status.ToString(),
            custodyEvent.Id,
            custodyEvent.EventType.ToString(),
            incident.SearchDeadline);
    }
}
