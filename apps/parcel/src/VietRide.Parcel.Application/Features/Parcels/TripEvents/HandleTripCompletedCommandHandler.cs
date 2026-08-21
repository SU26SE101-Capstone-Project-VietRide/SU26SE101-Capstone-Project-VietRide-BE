using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleTripCompletedCommandHandler
    : IRequestHandler<HandleTripCompletedCommand, int>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IParcelReliabilityRepository? _reliability;
    private readonly IIntegrationEventOutbox? _outbox;
    private readonly IClock _clock;

    public HandleTripCompletedCommandHandler(
        IParcelRepository parcelRepository,
        IParcelReliabilityRepository? reliability = null,
        IIntegrationEventOutbox? outbox = null,
        IClock? clock = null)
    {
        _parcelRepository = parcelRepository;
        _reliability = reliability;
        _outbox = outbox;
        _clock = clock ?? new SystemClock();
    }

    public async Task<int> Handle(
        HandleTripCompletedCommand command,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var updated = await _parcelRepository.TryBulkSetPendingOperatorActionByTripIdAsync(
            command.TripId,
            now,
            cancellationToken);

        if (_reliability is not null)
        {
            foreach (var parcel in updated)
            {
                var parcelEntity = await _parcelRepository.GetByIdAsync(parcel.ParcelId, cancellationToken);
                var existing = await _reliability.GetOpenIncidentAsync(
                    parcel.ParcelId,
                    ParcelIncidentType.MISSING,
                    cancellationToken);
                if (existing is not null)
                    continue;

                var current = await _reliability.GetCurrentCustodyAsync(parcel.ParcelId, cancellationToken);
                var leg = await _reliability.GetActiveLegAsync(parcel.ParcelId, cancellationToken);
                var incident = ParcelIncident.Open(
                    parcel.ParcelId,
                    parcel.OperatorId,
                    ParcelIncidentType.MISSING,
                    now.AddHours(parcelEntity?.SearchSlaHoursSnapshot > 0
                        ? parcelEntity.SearchSlaHoursSnapshot
                        : ParcelCompensationPolicy.DefaultSearchSlaHours),
                    parcel.TripId,
                    leg?.Id,
                    null,
                    "SYSTEM",
                    parcelEntity?.DropoffStopId.HasValue == true
                        ? $"STOP:{parcelEntity.DropoffStopId:D}"
                        : parcelEntity?.TripSnapshotDestinationStationName,
                    current?.LastLocationSnapshot,
                    "Trip completed while the parcel was still loaded or in transit.",
                    null,
                    operatorProcessBreach: true);
                incident.StartSearch();
                await _reliability.AddIncidentAsync(incident, cancellationToken);
                await _reliability.AddSearchTaskAsync(
                    ParcelSearchTask.Create(
                        incident.Id,
                        parcel.ParcelId,
                        ParcelSearchTaskType.VEHICLE_SWEEP,
                        current?.LastLocationSnapshot,
                        null,
                        now.AddMinutes(30)),
                    cancellationToken);
                await _reliability.AddSearchTaskAsync(
                    ParcelSearchTask.Create(
                        incident.Id,
                        parcel.ParcelId,
                        ParcelSearchTaskType.MANIFEST_RECONCILIATION,
                        current?.LastLocationSnapshot,
                        null,
                        now.AddHours(2)),
                    cancellationToken);

                if (_outbox is not null)
                {
                    await ParcelOutboxEvents.EnqueueAsync(
                        _outbox,
                        ParcelOutboxEvents.IncidentOpened,
                        new
                        {
                            incidentId = incident.Id,
                            parcelId = parcel.ParcelId,
                            operatorId = parcel.OperatorId,
                            tripId = parcel.TripId,
                            type = incident.Type.ToString(),
                            searchDeadline = incident.SearchDeadline,
                            source = "TRIP_COMPLETED",
                        },
                        cancellationToken);
                }
            }
        }

        return updated.Count;
    }
}
