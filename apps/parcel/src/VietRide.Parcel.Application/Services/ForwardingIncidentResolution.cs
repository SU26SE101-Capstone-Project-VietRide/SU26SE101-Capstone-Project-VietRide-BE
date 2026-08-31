using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Services;

internal static class ForwardingIncidentResolution
{
    public const string ResolutionCode = "FORWARDED_TO_EXPECTED_DROPOFF";

    public static async Task ResolveVerifiedUnloadsAsync(
        IReadOnlyCollection<Guid> verifiedParcelIds,
        IParcelReliabilityRepository reliability,
        IIntegrationEventOutbox outbox,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (verifiedParcelIds.Count == 0)
            return;

        var incidents = await reliability.ListActiveIncidentsByParcelsAsync(
            verifiedParcelIds,
            cancellationToken);
        foreach (var incident in incidents.Where(candidate =>
                     candidate.Status == ParcelIncidentStatus.FORWARDING))
        {
            await ResolveAsync(incident, reliability, outbox, now, cancellationToken);
        }
    }

    public static async Task ResolveAsync(
        ParcelIncident incident,
        IParcelReliabilityRepository reliability,
        IIntegrationEventOutbox outbox,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (incident.Status != ParcelIncidentStatus.FORWARDING)
            return;

        incident.Resolve(
            ResolutionCode,
            "Forwarded Parcel was unloaded at its expected drop-off location.",
            now);
        await reliability.UpdateIncidentAsync(incident, cancellationToken);

        var tasks = await reliability.ListSearchTasksAsync(incident.Id, cancellationToken);
        foreach (var task in tasks.Where(task =>
                     task.Status is ParcelSearchTaskStatus.OPEN or ParcelSearchTaskStatus.IN_PROGRESS))
        {
            task.Cancel(now);
            await reliability.UpdateSearchTaskAsync(task, cancellationToken);
        }

        await ParcelOutboxEvents.EnqueueAsync(
            outbox,
            ParcelOutboxEvents.IncidentUpdated,
            new
            {
                incidentId = incident.Id,
                parcelId = incident.ParcelId,
                operatorId = incident.OperatorId,
                status = incident.Status.ToString(),
                resolutionCode = ResolutionCode,
            },
            cancellationToken);
    }
}
