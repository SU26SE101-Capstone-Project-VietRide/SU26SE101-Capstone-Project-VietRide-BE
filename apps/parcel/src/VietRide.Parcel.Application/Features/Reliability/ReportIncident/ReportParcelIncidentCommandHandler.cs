using System.Text.Json;
using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.ReportIncident;

public sealed class ReportParcelIncidentCommandHandler
    : IRequestHandler<ReportParcelIncidentCommand, ReportParcelIncidentResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelCustodyService _custody;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ReportParcelIncidentCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IParcelCustodyService custody,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _custody = custody;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ReportParcelIncidentResponse> Handle(
        ReportParcelIncidentCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.AcquireForIncidentReportAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");

        var authorized = command.ReporterUserId == parcel.SenderUserId
            || command.ReporterUserId == parcel.RecipientUserId
            || command.OperatorId == parcel.OperatorId;
        if (!authorized)
            throw new ForbiddenException("FORBIDDEN", "Caller is not authorized to report this Parcel incident.");

        if (!Enum.TryParse<ParcelIncidentType>(command.IncidentType, true, out var type))
            throw new CodedValidationException("VALIDATION_ERROR", "IncidentType is invalid.");

        var isSameTenantOperator = command.OperatorId == parcel.OperatorId;
        if (!isSameTenantOperator
            && type is not (ParcelIncidentType.DELIVERY_NOT_RECEIVED
                or ParcelIncidentType.DAMAGED
                or ParcelIncidentType.PARTIAL_LOSS))
        {
            throw new CodedValidationException(
                "PARCEL_INCIDENT_TYPE_NOT_REPORTABLE",
                "Passengers can only report delivery-not-received, damaged, or partial-loss incidents.");
        }

        if (!isSameTenantOperator && !ParcelIncidentReportPolicy.CanPassengerReport(parcel.Status))
            throw ParcelIncidentReportPolicy.StatusNotReportable(parcel.Status, type);

        var existing = await _reliability.GetOpenIncidentAsync(parcel.Id, type, cancellationToken);
        if (existing is not null)
            throw new CodedConflictException("PARCEL_INCIDENT_ALREADY_OPEN", "An open incident already exists for this Parcel.");

        var now = _clock.UtcNow;
        var current = await _reliability.GetCurrentCustodyAsync(parcel.Id, cancellationToken);
        var eventIdempotencyKey = $"incident:{command.ReporterUserId:D}:{now:O}";
        var custodyEvent = await _custody.AppendAsync(
            parcel,
            ParcelCustodyEventType.EXCEPTION_REPORTED,
            current?.LastLocationType,
            current?.LastLocationId,
            current?.LastLocationSnapshot,
            command.ReporterUserId,
            command.OperatorId == parcel.OperatorId ? "OPERATOR" : "USER",
            "INCIDENT_REPORT",
            eventIdempotencyKey,
            command.EvidenceUrls,
            command.Description,
            cancellationToken);

        var incident = ParcelIncident.Open(
            parcel.Id,
            parcel.OperatorId,
            type,
            now.AddHours(parcel.SearchSlaHoursSnapshot > 0
                ? parcel.SearchSlaHoursSnapshot
                : ParcelCompensationPolicy.DefaultSearchSlaHours),
            parcel.TripId,
            custodyEvent.LegId,
            command.ReporterUserId,
            command.OperatorId == parcel.OperatorId ? "OPERATOR" : "USER",
            parcel.DropoffStopId.HasValue ? $"STOP:{parcel.DropoffStopId:D}" : parcel.TripSnapshotDestinationStationName,
            current?.LastLocationSnapshot,
            command.Description,
            command.EvidenceUrls is null ? null : JsonSerializer.Serialize(command.EvidenceUrls),
            operatorProcessBreach: false);
        incident.StartSearch();
        await _reliability.AddIncidentAsync(incident, cancellationToken);

        foreach (var taskType in new[]
        {
            ParcelSearchTaskType.MANIFEST_RECONCILIATION,
            ParcelSearchTaskType.STATION_INVENTORY,
            ParcelSearchTaskType.CREW_CONFIRMATION,
        })
        {
            await _reliability.AddSearchTaskAsync(
                ParcelSearchTask.Create(
                    incident.Id,
                    parcel.Id,
                    taskType,
                    current?.LastLocationSnapshot,
                    null,
                    now.AddHours(2)),
                cancellationToken);
        }

        var quarantined = await _parcels.TrySetPendingOperatorActionAsync(
            parcel.Id,
            PendingActionType.CUSTODY_EXCEPTION,
            command.Description ?? "Parcel incident reported.",
            null,
            now,
            cancellationToken,
            parcel.Status);
        if (!quarantined)
        {
            if (!isSameTenantOperator)
                throw ParcelIncidentReportPolicy.StatusNotReportable(parcel.Status, type);
            throw new CodedConflictException(
                "PARCEL_STATE_CONFLICT",
                "Parcel status changed while the incident report was being created.");
        }

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
                reporterUserId = command.ReporterUserId,
            },
            cancellationToken);

        return new ReportParcelIncidentResponse(
            incident.Id,
            parcel.Id,
            incident.Type.ToString(),
            incident.Status.ToString(),
            incident.SearchDeadline!.Value);
    }
}
