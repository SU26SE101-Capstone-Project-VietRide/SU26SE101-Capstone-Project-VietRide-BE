using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Reliability.CustodyException;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class DeclareIncidentLostCommandHandler
    : IRequestHandler<DeclareIncidentLostCommand, ParcelIncidentListItem>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelCustodyExceptionRequestRepository _custodyExceptionRequests;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public DeclareIncidentLostCommandHandler(
        IParcelReliabilityRepository reliability,
        IParcelCustodyExceptionRequestRepository custodyExceptionRequests,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _reliability = reliability;
        _custodyExceptionRequests = custodyExceptionRequests;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ParcelIncidentListItem> Handle(
        DeclareIncidentLostCommand request,
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

        var now = _clock.UtcNow;
        if (!incident.SearchDeadline.HasValue || now < incident.SearchDeadline.Value)
            throw new CodedConflictException("PARCEL_SEARCH_SLA_NOT_EXPIRED", "Search SLA has not expired.");

        if (incident.Status is ParcelIncidentStatus.OPEN or ParcelIncidentStatus.SEARCHING)
            incident.Escalate(now);
        if (incident.Status == ParcelIncidentStatus.ESCALATED)
            incident.ExpireSearch();
        if (incident.Status != ParcelIncidentStatus.SEARCH_EXPIRED)
            throw new CodedConflictException(
                "PARCEL_INCIDENT_INVALID_STATUS",
                "Only an active expired search can be declared lost.");
        incident.ConfirmLost(request.Note, now);
        await _reliability.UpdateIncidentAsync(incident, cancellationToken);
        await ParcelIncidentSearchTaskLifecycle.FailOutstandingAsync(
            _reliability,
            incident.Id,
            "Search completed without a verified found event.",
            now,
            cancellationToken);
        var activeLeg = await _reliability.GetActiveLegAsync(incident.ParcelId, cancellationToken);
        if (activeLeg is not null)
        {
            activeLeg.MarkLost(now);
            await _reliability.UpdateTransitLegAsync(activeLeg, cancellationToken);
        }
        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.IncidentUpdated,
            new { incidentId = incident.Id, parcelId = incident.ParcelId, status = incident.Status.ToString(), actorUserId = request.ActorUserId },
            cancellationToken);

        return MarkIncidentFoundCommandHandler.ToItem(incident);
    }
}
