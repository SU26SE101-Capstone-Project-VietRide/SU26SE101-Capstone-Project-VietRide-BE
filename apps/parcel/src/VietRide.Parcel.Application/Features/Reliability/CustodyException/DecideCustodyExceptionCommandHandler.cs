using System.Text.Json;
using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Reliability.Incidents;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed class DecideCustodyExceptionCommandHandler
    : IRequestHandler<DecideCustodyExceptionCommand, ReportCustodyExceptionResponse>
{
    private readonly IParcelCustodyExceptionRequestRepository _requests;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelRepository _parcels;
    private readonly IParcelCustodyService _custody;
    private readonly ITripServiceClient _trips;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public DecideCustodyExceptionCommandHandler(
        IParcelCustodyExceptionRequestRepository requests,
        IParcelReliabilityRepository reliability,
        IParcelRepository parcels,
        IParcelCustodyService custody,
        ITripServiceClient trips,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _requests = requests;
        _reliability = reliability;
        _parcels = parcels;
        _custody = custody;
        _trips = trips;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ReportCustodyExceptionResponse> Handle(
        DecideCustodyExceptionCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.SubjectType == "PARCEL"
            ? await _requests.GetLatestByParcelForUpdateAsync(command.SubjectId, cancellationToken)
            : await _requests.GetByIncidentForUpdateAsync(command.SubjectId, cancellationToken);
        if (request is null || request.OperatorId != command.OperatorId)
            throw new CodedNotFoundException(
                "PARCEL_CUSTODY_EXCEPTION_REQUEST_NOT_FOUND",
                "Custody exception approval request was not found.");
        if (request.Status != ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL)
            throw new CodedConflictException(
                "PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED",
                "Custody exception request has already been decided.");

        if (command.ReviewerRole == "DRIVER")
            await AuthorizeAssignedDriverAsync(request, command, cancellationToken);

        var parcel = await _parcels.GetByIdAsync(request.ParcelId, cancellationToken);
        var incident = await _reliability.GetIncidentAsync(request.IncidentId, cancellationToken);
        if (parcel is null || incident is null
            || parcel.OperatorId != command.OperatorId
            || incident.OperatorId != command.OperatorId)
            throw new CodedNotFoundException(
                "PARCEL_CUSTODY_EXCEPTION_REQUEST_NOT_FOUND",
                "Custody exception approval request was not found.");

        var now = _clock.UtcNow;
        if (command.Decision == "APPROVE")
        {
            var evidence = JsonSerializer.Deserialize<string[]>(request.EvidenceReferencesJson)
                ?? Array.Empty<string>();
            var custodyEvent = await _custody.AppendAsync(
                parcel,
                ParcelCustodyEventType.MANUAL_CUSTODY_EXCEPTION,
                request.ActualLocationType,
                request.ActualLocationId,
                request.LocationSnapshot,
                request.ReportedByUserId,
                request.ReportedByRole,
                "APPROVED_CUSTODY_EXCEPTION",
                command.IdempotencyKey.ToString("D"),
                evidence,
                request.Reason,
                cancellationToken);
            incident.MarkOperatorProcessBreach();
            var searchSlaHours = parcel.SearchSlaHoursSnapshot > 0
                ? parcel.SearchSlaHoursSnapshot
                : ParcelCompensationPolicy.DefaultSearchSlaHours;
            incident.StartSearch(now.AddHours(searchSlaHours));
            await _reliability.AddSearchTaskAsync(
                ParcelSearchTask.Create(
                    incident.Id,
                    parcel.Id,
                    ParcelSearchTaskType.MANIFEST_RECONCILIATION,
                    request.LocationSnapshot,
                    null,
                    now.AddMinutes(30)),
                cancellationToken);
            await _reliability.AddSearchTaskAsync(
                ParcelSearchTask.Create(
                    incident.Id,
                    parcel.Id,
                    ParcelSearchTaskType.VEHICLE_SWEEP,
                    request.LocationSnapshot,
                    null,
                    now.AddHours(2)),
                cancellationToken);
            request.Approve(
                command.ReviewerUserId,
                command.ReviewerRole,
                command.Note,
                custodyEvent.Id,
                now);
            await _reliability.UpdateIncidentAsync(incident, cancellationToken);
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.IncidentOpened,
                new
                {
                    incidentId = incident.Id,
                    parcelId = incident.ParcelId,
                    operatorId = incident.OperatorId,
                    type = incident.Type.ToString(),
                    source = "CUSTODY_EXCEPTION_APPROVED",
                    reviewerUserId = command.ReviewerUserId,
                    reviewerRole = command.ReviewerRole,
                    searchDeadline = incident.SearchDeadline,
                    lastKnownLocation = request.LocationSnapshot,
                },
                cancellationToken);
            return CustodyExceptionResponseMapper.Map(request, incident, ["CONTINUE_SEARCH"]);
        }

        request.Reject(command.ReviewerUserId, command.ReviewerRole, command.Note, now);
        incident.RejectReport(command.Note, now);
        await _reliability.UpdateIncidentAsync(incident, cancellationToken);
        await ParcelIncidentSearchTaskLifecycle.CancelOutstandingAsync(
            _reliability,
            incident.Id,
            now,
            cancellationToken);
        var restored = await _parcels.TryResolvePendingOperatorActionAsync(
            parcel.Id,
            PendingActionType.CUSTODY_EXCEPTION,
            now,
            cancellationToken);
        if (restored is null)
            throw new CodedConflictException(
                "INVALID_STATUS",
                "Parcel is no longer waiting for custody exception approval.");
        return CustodyExceptionResponseMapper.Map(request, incident, []);
    }

    private async Task AuthorizeAssignedDriverAsync(
        ParcelCustodyExceptionRequest request,
        DecideCustodyExceptionCommand command,
        CancellationToken cancellationToken)
    {
        var outcome = await _trips.AuthorizeCrewForTripAsync(
            request.TripId,
            command.ReviewerUserId,
            command.OperatorId,
            "DRIVER",
            cancellationToken);
        if (outcome.Kind == TripCrewAuthorizationOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip service is unavailable.");
        if (outcome.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
            throw new ForbiddenException("FORBIDDEN", "Only the assigned Driver can review this report.");
    }

}
