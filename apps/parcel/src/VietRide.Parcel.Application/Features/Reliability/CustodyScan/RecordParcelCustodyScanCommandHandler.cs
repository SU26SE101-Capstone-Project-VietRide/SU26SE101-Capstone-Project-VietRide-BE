using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyScan;

public sealed class RecordParcelCustodyScanCommandHandler
    : IRequestHandler<RecordParcelCustodyScanCommand, ParcelCustodyScanResponse>
{
    private static readonly HashSet<ParcelCustodyEventType> AllowedScanEvents =
    [
        ParcelCustodyEventType.ACCEPTED,
        ParcelCustodyEventType.ARRIVED_AT_STOP,
        ParcelCustodyEventType.HANDOFF,
        ParcelCustodyEventType.RETURNED_TO_STATION,
    ];

    private readonly IParcelRepository _parcels;
    private readonly IParcelCustodyService _custody;
    private readonly ITripServiceClient _trips;

    public RecordParcelCustodyScanCommandHandler(
        IParcelRepository parcels,
        IParcelCustodyService custody,
        ITripServiceClient trips)
    {
        _parcels = parcels;
        _custody = custody;
        _trips = trips;
    }

    public async Task<ParcelCustodyScanResponse> Handle(
        RecordParcelCustodyScanCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");
        if (!string.Equals(parcel.ParcelCode, command.ParcelCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new CodedConflictException(
                "SCAN_IDENTITY_MISMATCH",
                "The scanned QR code does not belong to this parcel.",
                [new ValidationError("requiredAction", "VERIFY_PARCEL_IDENTITY")]);
        if (!Enum.TryParse<ParcelCustodyEventType>(command.EventType, true, out var eventType)
            || !AllowedScanEvents.Contains(eventType))
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "eventType is not permitted for a direct custody scan.");
        if (!Enum.TryParse<ParcelCustodyLocationType>(command.ActualLocationType, true, out var locationType))
            throw new CodedValidationException("PARCEL_CUSTODY_LOCATION_REQUIRED", "Actual location type is invalid.");
        if (locationType != ParcelCustodyLocationType.VEHICLE && !command.ActualLocationId.HasValue)
            throw new CodedValidationException(
                "PARCEL_CUSTODY_LOCATION_REQUIRED",
                "A location id is required for this custody location type.");

        if (command.RequireAssignedCrew)
        {
            var authorization = await _trips.AuthorizeAssistantForTripAsync(
                parcel.TripId,
                command.ActorUserId,
                command.OperatorId,
                cancellationToken);
            if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
                throw new ForbiddenException("FORBIDDEN", "Only the assigned assistant can scan this parcel.");
        }

        await ValidatePhysicalLocationAsync(
            parcel,
            eventType,
            locationType,
            command.ActualLocationId,
            cancellationToken);

        var custodyEvent = await _custody.AppendAsync(
            parcel,
            eventType,
            locationType,
            command.ActualLocationId,
            command.LocationSnapshot,
            command.ActorUserId,
            command.ActorRole,
            "CUSTODY_SCAN",
            command.IdempotencyKey.ToString("D"),
            command.EvidenceReferences,
            command.Reason,
            cancellationToken);
        return new ParcelCustodyScanResponse(
            custodyEvent.Id,
            parcel.Id,
            custodyEvent.EventType.ToString(),
            custodyEvent.ActualLocationType?.ToString(),
            custodyEvent.ActualLocationId,
            custodyEvent.OccurredAt,
            custodyEvent.Sequence);
    }

    private async Task ValidatePhysicalLocationAsync(
        Domain.Entities.Parcel parcel,
        ParcelCustodyEventType eventType,
        ParcelCustodyLocationType locationType,
        Guid? locationId,
        CancellationToken cancellationToken)
    {
        var tripOutcome = await _trips.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
        if (tripOutcome.Kind == TripSnapshotOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                tripOutcome.ErrorMessage ?? "Trip service is unavailable.");
        if (tripOutcome.Kind != TripSnapshotOutcomeKind.Success || tripOutcome.Snapshot is null)
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        var trip = tripOutcome.Snapshot;
        if (trip.OperatorId != parcel.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Trip does not belong to this operator.");

        switch (eventType)
        {
            case ParcelCustodyEventType.ACCEPTED:
                EnsureLocation(
                    locationType == ParcelCustodyLocationType.ORIGIN_STATION
                    && locationId == trip.OriginStation.Id,
                    trip.OriginStation.Id,
                    locationId,
                    "SCAN_AT_TRIP_ORIGIN");
                if (parcel.Status is ParcelStatus.LOADED
                    or ParcelStatus.IN_TRANSIT
                    or ParcelStatus.UNLOADED
                    or ParcelStatus.DELIVERED_PENDING_CONFIRM
                    or ParcelStatus.DELIVERY_CONFIRMED)
                    throw new CodedConflictException(
                        "INVALID_STATUS",
                        "An accepted custody scan is only valid before the Parcel is loaded.");
                return;

            case ParcelCustodyEventType.ARRIVED_AT_STOP:
                await EnsureCurrentRouteStopAsync(parcel.TripId, locationType, locationId, cancellationToken);
                return;

            case ParcelCustodyEventType.HANDOFF:
                if (locationType == ParcelCustodyLocationType.VEHICLE)
                {
                    EnsureLocation(
                        locationId == trip.VehicleId,
                        trip.VehicleId,
                        locationId,
                        "USE_ASSIGNED_TRIP_VEHICLE");
                    return;
                }
                if (locationType == ParcelCustodyLocationType.ORIGIN_STATION)
                {
                    EnsureLocation(
                        locationId == trip.OriginStation.Id
                        && trip.Status is "SCHEDULED" or "BOARDING",
                        trip.OriginStation.Id,
                        locationId,
                        "HANDOFF_AT_TRIP_ORIGIN");
                    return;
                }
                if (locationType == ParcelCustodyLocationType.DESTINATION_STATION)
                {
                    EnsureLocation(
                        locationId == trip.DestinationStation.Id && trip.DestinationArrivedAt.HasValue,
                        trip.DestinationStation.Id,
                        locationId,
                        "ARRIVE_DESTINATION_BEFORE_HANDOFF");
                    return;
                }
                await EnsureCurrentRouteStopAsync(parcel.TripId, locationType, locationId, cancellationToken);
                return;

            case ParcelCustodyEventType.RETURNED_TO_STATION:
                var isTripStation = (locationType is ParcelCustodyLocationType.ORIGIN_STATION
                        or ParcelCustodyLocationType.DESTINATION_STATION)
                    && (locationId == trip.OriginStation.Id || locationId == trip.DestinationStation.Id);
                EnsureLocation(
                    isTripStation
                    && parcel.Status is ParcelStatus.RETURN_INITIATED or ParcelStatus.RETURNED,
                    trip.OriginStation.Id,
                    locationId,
                    "USE_RETURN_FLOW_STATION");
                return;
        }
    }

    private async Task EnsureCurrentRouteStopAsync(
        Guid tripId,
        ParcelCustodyLocationType locationType,
        Guid? locationId,
        CancellationToken cancellationToken)
    {
        var outcome = await _trips.GetTripOperationalLocationAsync(tripId, cancellationToken);
        if (outcome.Kind == TripOperationalLocationOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip operational location is unavailable.");
        if (outcome.Kind != TripOperationalLocationOutcomeKind.Success || outcome.Snapshot is null)
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var operational = outcome.Snapshot;
        EnsureLocation(
            locationType == ParcelCustodyLocationType.ROUTE_STOP
            && locationId == operational.CurrentStopId
            && string.Equals(operational.CurrentStopStatus, "ARRIVED", StringComparison.OrdinalIgnoreCase)
            && !operational.ActualDepartureAt.HasValue,
            operational.CurrentStopId,
            locationId,
            "SCAN_AT_CURRENT_OPERATIONAL_STOP");
    }

    private static void EnsureLocation(
        bool valid,
        Guid? expectedLocationId,
        Guid? actualLocationId,
        string requiredAction)
    {
        if (valid)
            return;
        throw new CodedConflictException(
            "PARCEL_CUSTODY_LOCATION_MISMATCH",
            "The custody scan location does not match the Trip operational location.",
            [
                new ValidationError("expectedLocationId", expectedLocationId?.ToString("D") ?? string.Empty),
                new ValidationError("actualLocationId", actualLocationId?.ToString("D") ?? string.Empty),
                new ValidationError("requiredAction", requiredAction),
            ]);
    }
}
