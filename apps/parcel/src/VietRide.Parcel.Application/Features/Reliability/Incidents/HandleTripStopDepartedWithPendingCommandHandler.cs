using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class HandleTripStopDepartedWithPendingCommandHandler
    : IRequestHandler<HandleTripStopDepartedWithPendingCommand, int>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IIntegrationEventOutbox _outbox;

    public HandleTripStopDepartedWithPendingCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IIntegrationEventOutbox outbox)
    {
        _parcels = parcels;
        _reliability = reliability;
        _outbox = outbox;
    }

    public async Task<int> Handle(
        HandleTripStopDepartedWithPendingCommand command,
        CancellationToken cancellationToken)
    {
        var candidates = await _parcels.ListPendingDropoffByTripAndStopAsync(
            command.TripId,
            command.StopId,
            cancellationToken);
        var opened = 0;

        foreach (var parcel in candidates)
        {
            var existing = await _reliability.GetOpenIncidentAsync(
                parcel.Id,
                ParcelIncidentType.MISSING_AFTER_DEPARTURE,
                cancellationToken);
            if (existing is not null)
                continue;
            var unresolvedHandoff = await _reliability.GetOpenIncidentAsync(
                parcel.Id,
                ParcelIncidentType.UNSCANNED_HANDOFF,
                cancellationToken);
            if (unresolvedHandoff is not null)
            {
                await _parcels.TrySetPendingOperatorActionAsync(
                    parcel.Id,
                    PendingActionType.CUSTODY_EXCEPTION,
                    "Expected stop departed with an unresolved handoff reconciliation.",
                    null,
                    command.DepartedAt,
                    cancellationToken,
                    parcel.Status);
                continue;
            }

            if (!await _parcels.TrySetPendingOperatorActionAsync(
                    parcel.Id,
                    PendingActionType.CUSTODY_EXCEPTION,
                    "Expected stop departed without confirmed parcel unload.",
                    null,
                    command.DepartedAt,
                    cancellationToken,
                    parcel.Status))
                continue;

            var current = await _reliability.GetCurrentCustodyAsync(parcel.Id, cancellationToken);
            var leg = await _reliability.GetActiveLegAsync(parcel.Id, cancellationToken);
            var incident = ParcelIncident.Open(
                parcel.Id,
                parcel.OperatorId,
                ParcelIncidentType.MISSING_AFTER_DEPARTURE,
                command.DepartedAt.AddHours(parcel.SearchSlaHoursSnapshot > 0
                    ? parcel.SearchSlaHoursSnapshot
                    : ParcelCompensationPolicy.DefaultSearchSlaHours),
                parcel.TripId,
                leg?.Id,
                null,
                "SYSTEM",
                $"STOP:{command.StopId:D}",
                current?.LastLocationSnapshot,
                "Trip departed the expected drop-off stop without a confirmed unload event.",
                null,
                operatorProcessBreach: true);
            incident.StartSearch();
            await _reliability.AddIncidentAsync(incident, cancellationToken);
            await _reliability.AddSearchTaskAsync(
                ParcelSearchTask.Create(
                    incident.Id,
                    parcel.Id,
                    ParcelSearchTaskType.VEHICLE_SWEEP,
                    current?.LastLocationSnapshot,
                    null,
                    command.DepartedAt.AddMinutes(30)),
                cancellationToken);
            await _reliability.AddSearchTaskAsync(
                ParcelSearchTask.Create(
                    incident.Id,
                    parcel.Id,
                    ParcelSearchTaskType.STATION_INVENTORY,
                    $"STOP:{command.StopId:D}",
                    null,
                    command.DepartedAt.AddHours(2)),
                cancellationToken);
            await _reliability.AddSearchTaskAsync(
                ParcelSearchTask.Create(
                    incident.Id,
                    parcel.Id,
                    ParcelSearchTaskType.MANIFEST_RECONCILIATION,
                    $"TRIP:{command.TripId:D}",
                    null,
                    command.DepartedAt.AddHours(2)),
                cancellationToken);

            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.IncidentOpened,
                new
                {
                    incidentId = incident.Id,
                    parcelId = parcel.Id,
                    operatorId = parcel.OperatorId,
                    tripId = parcel.TripId,
                    stopId = command.StopId,
                    type = incident.Type.ToString(),
                    source = "TRIP_STOP_DEPARTED",
                    searchDeadline = incident.SearchDeadline,
                },
                cancellationToken);
            opened++;
        }

        return opened;
    }
}
