using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class HandleTripDestinationArrivedCommandHandler
    : IRequestHandler<HandleTripDestinationArrivedCommand, int>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IIntegrationEventOutbox _outbox;

    public HandleTripDestinationArrivedCommandHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IIntegrationEventOutbox outbox)
    {
        _parcels = parcels;
        _reliability = reliability;
        _outbox = outbox;
    }

    public async Task<int> Handle(
        HandleTripDestinationArrivedCommand command,
        CancellationToken cancellationToken)
    {
        var candidates = await _parcels.ListPendingTerminalDropoffByTripAsync(
            command.TripId,
            cancellationToken);
        var opened = 0;
        foreach (var parcel in candidates)
        {
            var existing = await _reliability.GetOpenIncidentAsync(
                parcel.Id,
                ParcelIncidentType.MISSING,
                cancellationToken);
            if (existing is not null)
                continue;

            if (!await _parcels.TrySetPendingOperatorActionAsync(
                    parcel.Id,
                    PendingActionType.CUSTODY_EXCEPTION,
                    "Destination arrived without confirmed terminal unload.",
                    null,
                    command.ArrivedAt,
                    cancellationToken,
                    parcel.Status))
                continue;

            var current = await _reliability.GetCurrentCustodyAsync(parcel.Id, cancellationToken);
            var leg = await _reliability.GetActiveLegAsync(parcel.Id, cancellationToken);
            var incident = ParcelIncident.Open(
                parcel.Id,
                parcel.OperatorId,
                ParcelIncidentType.MISSING,
                command.ArrivedAt.AddHours(parcel.SearchSlaHoursSnapshot > 0
                    ? parcel.SearchSlaHoursSnapshot
                    : ParcelCompensationPolicy.DefaultSearchSlaHours),
                parcel.TripId,
                leg?.Id,
                null,
                "SYSTEM",
                $"DESTINATION_STATION:{command.DestinationStationId:D}",
                current?.LastLocationSnapshot,
                "Trip reached its destination while the parcel was still loaded or in transit.",
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
                    command.ArrivedAt.AddMinutes(30)),
                cancellationToken);
            await _reliability.AddSearchTaskAsync(
                ParcelSearchTask.Create(
                    incident.Id,
                    parcel.Id,
                    ParcelSearchTaskType.STATION_INVENTORY,
                    $"DESTINATION_STATION:{command.DestinationStationId:D}",
                    null,
                    command.ArrivedAt.AddHours(2)),
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
                    destinationStationId = command.DestinationStationId,
                    type = incident.Type.ToString(),
                    source = "TRIP_DESTINATION_ARRIVED",
                    searchDeadline = incident.SearchDeadline,
                },
                cancellationToken);
            opened++;
        }

        return opened;
    }
}
