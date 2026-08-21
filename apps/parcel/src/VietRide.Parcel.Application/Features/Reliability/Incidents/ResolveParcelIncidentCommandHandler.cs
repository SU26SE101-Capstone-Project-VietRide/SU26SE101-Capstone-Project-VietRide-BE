using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class ResolveParcelIncidentCommandHandler
    : IRequestHandler<ResolveParcelIncidentCommand, ParcelIncidentListItem>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ResolveParcelIncidentCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _parcels = parcels;
        _reliability = reliability;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ParcelIncidentListItem> Handle(
        ResolveParcelIncidentCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ResolutionCode))
            throw new CodedValidationException("VALIDATION_ERROR", "resolutionCode is required.");
        var incident = await _reliability.GetIncidentAsync(command.IncidentId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Incident was not found.");
        if (incident.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Incident does not belong to this operator.");
        if (incident.Status is not (ParcelIncidentStatus.FOUND or ParcelIncidentStatus.FORWARDING))
            throw new CodedConflictException(
                "PARCEL_INCIDENT_INVALID_STATUS",
                "Only a found or forwarding incident can be resolved.");

        var now = _clock.UtcNow;
        incident.Resolve(command.ResolutionCode, command.Note, now);
        await _reliability.UpdateIncidentAsync(incident, cancellationToken);
        await ParcelIncidentSearchTaskLifecycle.CancelOutstandingAsync(
            _reliability,
            incident.Id,
            now,
            cancellationToken);
        await _parcels.TryResolvePendingOperatorActionAsync(
            incident.ParcelId,
            PendingActionType.CUSTODY_EXCEPTION,
            now,
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
                resolutionCode = command.ResolutionCode,
                actorUserId = command.ActorUserId,
            },
            cancellationToken);
        return MarkIncidentFoundCommandHandler.ToItem(incident);
    }
}
