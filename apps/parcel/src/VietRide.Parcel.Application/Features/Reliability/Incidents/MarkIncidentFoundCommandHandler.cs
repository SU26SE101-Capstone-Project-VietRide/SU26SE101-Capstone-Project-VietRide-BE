using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Reliability.CustodyException;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class MarkIncidentFoundCommandHandler
    : IRequestHandler<MarkIncidentFoundCommand, ParcelIncidentListItem>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelRepository _parcels;
    private readonly IParcelCustodyExceptionRequestRepository _custodyExceptionRequests;
    private readonly IParcelCustodyService _custody;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public MarkIncidentFoundCommandHandler(
        IParcelReliabilityRepository reliability,
        IParcelRepository parcels,
        IParcelCustodyExceptionRequestRepository custodyExceptionRequests,
        IParcelCustodyService custody,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _reliability = reliability;
        _parcels = parcels;
        _custodyExceptionRequests = custodyExceptionRequests;
        _custody = custody;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ParcelIncidentListItem> Handle(
        MarkIncidentFoundCommand request,
        CancellationToken cancellationToken)
    {
        var incident = await _reliability.GetIncidentAsync(request.IncidentId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Incident was not found.");
        if (incident.OperatorId != request.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Incident does not belong to this operator.");
        await CustodyExceptionApprovalGuard.EnsureNotPendingAsync(
            _custodyExceptionRequests,
            incident.Id,
            cancellationToken);
        if (incident.Status is not (ParcelIncidentStatus.OPEN
            or ParcelIncidentStatus.SEARCHING
            or ParcelIncidentStatus.ESCALATED
            or ParcelIncidentStatus.SEARCH_EXPIRED))
            throw new CodedConflictException(
                "PARCEL_INCIDENT_INVALID_STATUS",
                "Only an active search incident can be marked found.");
        if (!Enum.TryParse<ParcelCustodyLocationType>(request.ActualLocationType, true, out var locationType))
            throw new CodedValidationException(
                "PARCEL_CUSTODY_LOCATION_REQUIRED",
                "The found location type is invalid.");
        if (locationType != ParcelCustodyLocationType.VEHICLE && !request.ActualLocationId.HasValue)
            throw new CodedValidationException(
                "PARCEL_CUSTODY_LOCATION_REQUIRED",
                "The found location id is required.");
        var parcel = await _parcels.GetByIdAsync(incident.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");

        var now = _clock.UtcNow;
        incident.MarkFound(request.Note);
        await _reliability.UpdateIncidentAsync(incident, cancellationToken);
        await ParcelIncidentSearchTaskLifecycle.CancelOutstandingAsync(
            _reliability,
            incident.Id,
            now,
            cancellationToken);
        await _custody.AppendAsync(
            parcel,
            ParcelCustodyEventType.FOUND,
            locationType,
            request.ActualLocationId,
            request.LocationSnapshot,
            request.ActorUserId,
            "OPERATOR_STAFF",
            "INCIDENT_FOUND",
            $"incident-found:{incident.Id:D}",
            request.EvidenceReferences,
            request.Note,
            cancellationToken);
        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.IncidentUpdated,
            new
            {
                incidentId = incident.Id,
                parcelId = incident.ParcelId,
                operatorId = incident.OperatorId,
                status = incident.Status.ToString(),
                actualLocationType = locationType.ToString(),
                actualLocationId = request.ActualLocationId,
                actorUserId = request.ActorUserId,
            },
            cancellationToken);

        return ToItem(incident);
    }

    internal static ParcelIncidentListItem ToItem(VietRide.Parcel.Domain.Entities.ParcelIncident incident)
        => new(
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
