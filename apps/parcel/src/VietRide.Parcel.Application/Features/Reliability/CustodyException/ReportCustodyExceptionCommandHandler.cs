using System.Text.Json;
using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed class ReportCustodyExceptionCommandHandler
    : IRequestHandler<ReportCustodyExceptionCommand, ReportCustodyExceptionResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelCustodyExceptionRequestRepository _requests;
    private readonly ITripServiceClient _trips;
    private readonly IClock _clock;

    public ReportCustodyExceptionCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IParcelCustodyExceptionRequestRepository requests,
        ITripServiceClient trips,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _requests = requests;
        _trips = trips;
        _clock = clock;
    }

    public async Task<ReportCustodyExceptionResponse> Handle(
        ReportCustodyExceptionCommand command,
        CancellationToken cancellationToken)
    {
        var replay = await _requests.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            if (replay.ParcelId != command.ParcelId
                || replay.ReportedByUserId != command.ActorUserId
                || replay.OperatorId != command.OperatorId)
                throw new CodedConflictException("RESOURCE_CONFLICT", "Idempotency key belongs to another custody exception request.");
            var replayIncident = await _reliability.GetIncidentAsync(replay.IncidentId, cancellationToken)
                ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Parcel incident was not found.");
            var replayActions = replay.Status switch
            {
                ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL => new[] { "WAIT_FOR_APPROVAL" },
                ParcelCustodyExceptionRequestStatus.APPROVED => new[] { "CONTINUE_SEARCH" },
                _ => Array.Empty<string>(),
            };
            return CustodyExceptionResponseMapper.Map(replay, replayIncident, replayActions);
        }

        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        var authorization = await _trips.AuthorizeAssistantForTripAsync(
            parcel.TripId,
            command.ActorUserId,
            command.OperatorId,
            cancellationToken);
        if (authorization.Kind == TripCrewAuthorizationOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                authorization.ErrorMessage ?? "Trip service is unavailable.");
        if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
            throw new ForbiddenException("FORBIDDEN", "Only the assigned assistant can report this exception.");

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
        var activeLeg = await _reliability.GetLatestTransitLegAsync(parcel.Id, cancellationToken);

        var incident = ParcelIncident.Open(
            parcel.Id,
            parcel.OperatorId,
            incidentType,
            now.AddHours(parcel.SearchSlaHoursSnapshot > 0
                ? parcel.SearchSlaHoursSnapshot
                : ParcelCompensationPolicy.DefaultSearchSlaHours),
            parcel.TripId,
            activeLeg?.Id,
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
            operatorProcessBreach: false);
        await _reliability.AddIncidentAsync(incident, cancellationToken);

        var approvalRequest = ParcelCustodyExceptionRequest.Create(
            parcel.Id,
            incident.Id,
            parcel.OperatorId,
            parcel.TripId,
            incidentType,
            locationType,
            command.ActualLocationId,
            command.LocationSnapshot,
            command.TemporaryExceptionTag,
            command.Description,
            command.ObservedWeightKg,
            JsonSerializer.Serialize(command.EvidenceUrls ?? Array.Empty<string>()),
            command.Reason,
            command.ActorUserId,
            command.ActorRole,
            now,
            command.IdempotencyKey);
        await _requests.AddAsync(approvalRequest, cancellationToken);

        var quarantined = await _parcels.TrySetPendingOperatorActionAsync(
            parcel.Id,
            PendingActionType.CUSTODY_EXCEPTION,
            command.Reason,
            null,
            now,
            cancellationToken,
            parcel.Status);
        if (!quarantined)
            throw new CodedConflictException("INVALID_STATUS", "Parcel status does not allow a custody exception report.");

        return CustodyExceptionResponseMapper.Map(approvalRequest, incident, ["WAIT_FOR_APPROVAL"]);
    }
}
