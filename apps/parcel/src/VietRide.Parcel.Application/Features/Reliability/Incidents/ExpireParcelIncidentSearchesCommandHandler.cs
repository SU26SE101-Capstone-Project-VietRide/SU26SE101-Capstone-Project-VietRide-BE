using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class ExpireParcelIncidentSearchesCommandHandler
    : IRequestHandler<ExpireParcelIncidentSearchesCommand, int>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ExpireParcelIncidentSearchesCommandHandler(
        IParcelReliabilityRepository reliability,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _reliability = reliability;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<int> Handle(
        ExpireParcelIncidentSearchesCommand command,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var incidents = await _reliability.ListExpiredSearchIncidentsAsync(now, command.MaxBatch, cancellationToken);
        foreach (var incident in incidents)
        {
            if (incident.Status is ParcelIncidentStatus.OPEN or ParcelIncidentStatus.SEARCHING)
                incident.Escalate(now);
            if (incident.Status == ParcelIncidentStatus.ESCALATED)
                incident.ExpireSearch();
            incident.ConfirmLost("Search SLA expired without a verified found event.", now);
            await _reliability.UpdateIncidentAsync(incident, cancellationToken);
            await ParcelIncidentSearchTaskLifecycle.FailOutstandingAsync(
                _reliability,
                incident.Id,
                "Search SLA expired without a verified found event.",
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
                new
                {
                    incidentId = incident.Id,
                    parcelId = incident.ParcelId,
                    operatorId = incident.OperatorId,
                    status = incident.Status.ToString(),
                    claimEligible = true,
                    source = "SEARCH_SLA_EXPIRED",
                },
                cancellationToken);
        }

        return incidents.Count;
    }
}
