using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface ITripServiceClient
{
    Task<TripCrewAuthorizationOutcome> AuthorizeAssistantForTripAsync(
        Guid tripId,
        Guid userId,
        Guid operatorId,
        CancellationToken cancellationToken = default);

    Task<TripCrewAuthorizationOutcome> AuthorizeCrewForTripAsync(
        Guid tripId,
        Guid userId,
        Guid operatorId,
        string role,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TripCrewAuthorizationOutcome(
            TripCrewAuthorizationOutcomeKind.Denied));

    Task<TripSnapshotOutcome> GetTripParcelSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);

    async Task<TripOperationalLocationOutcome> GetTripOperationalLocationAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await GetTripParcelSnapshotAsync(tripId, cancellationToken);
        if (outcome.Kind == TripSnapshotOutcomeKind.TripNotFound)
            return new TripOperationalLocationOutcome(
                TripOperationalLocationOutcomeKind.TripNotFound,
                null,
                outcome.ErrorMessage);
        if (outcome.Kind != TripSnapshotOutcomeKind.Success || outcome.Snapshot is null)
            return new TripOperationalLocationOutcome(
                TripOperationalLocationOutcomeKind.TransportError,
                null,
                outcome.ErrorMessage);

        var trip = outcome.Snapshot;
        var currentStop = trip.Stops
            .Where(stop => string.Equals(stop.Status, "ARRIVED", StringComparison.OrdinalIgnoreCase)
                && !stop.ActualDepartureTime.HasValue)
            .OrderByDescending(stop => stop.OrderIndex)
            .FirstOrDefault();
        return new TripOperationalLocationOutcome(
            TripOperationalLocationOutcomeKind.Success,
            new TripOperationalLocationSnapshot(
                trip.TripId,
                trip.VehicleId,
                trip.Status,
                currentStop?.StopId,
                currentStop?.Status,
                currentStop?.ActualArrivalTime,
                currentStop?.ActualDepartureTime,
                trip.DestinationArrivedAt),
            null);
    }

    Task<TripSummaryBatchOutcome> GetTripSummariesAsync(
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Trip summary batch lookup is not implemented by this client.");

    Task<TripForwardingOptionsOutcome> GetForwardingOptionsAsync(
        Guid operatorId,
        Guid? excludedTripId,
        string pickupLocationType,
        Guid pickupLocationId,
        string targetLocationType,
        Guid targetLocationId,
        decimal weightKg,
        decimal volumeM3,
        DateTimeOffset earliestDeparture,
        int limit,
        CancellationToken cancellationToken = default)
        => Task.FromResult(TripForwardingOptionsOutcome.Failure(
            "Forwarding option search is not implemented by this Trip client."));

    Task<RouteOwnershipOutcome> ValidateRouteOwnershipAsync(
        Guid routeId,
        Guid operatorId,
        CancellationToken cancellationToken = default);

    Task<RouteSearchOutcome> SearchRoutesAsync(
        Guid operatorId,
        string search,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RouteSearchOutcome.Failure("Trip route search is not implemented by this client."));

    Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        decimal estimatedVolumeM3,
        ParcelSizeCategory sizeCategory,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsForRoutesAsync(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        decimal estimatedVolumeM3,
        ParcelSizeCategory sizeCategory,
        IReadOnlyCollection<Guid> eligibleRouteIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => SearchAvailableParcelTripsAsync(
            originStationId,
            destinationStationId,
            departureDate,
            estimatedWeightKg,
            estimatedVolumeM3,
            sizeCategory,
            page,
            pageSize,
            cancellationToken);

    Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        ParcelSizeCategory sizeCategory,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> ReserveCargoWithOverrideAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> GetCargoCapacityAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> RemeasureCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow = false,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default);

    Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default);

    Task<TripCargoTransferOutcome> TransferCargoAsync(
        Guid sourceTripId,
        Guid parcelId,
        Guid targetTripId,
        string targetState,
        bool allowCapacityOverflow,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TripCargoTransferOutcome(
            TripCargoTransferOutcomeKind.TransportError,
            "Atomic cargo transfer is not supported by this Trip client."));
}
