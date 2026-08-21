using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class DevTripServiceClient : ITripServiceClient
{
    private readonly ILogger<DevTripServiceClient> _logger;

    public DevTripServiceClient(ILogger<DevTripServiceClient> logger)
    {
        _logger = logger;
    }

    public Task<TripCrewAuthorizationOutcome> AuthorizeAssistantForTripAsync(
        Guid tripId,
        Guid userId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for AuthorizeAssistantForTripAsync({TripId}, {UserId}).", tripId, userId);
        return Task.FromResult(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
    }

    public Task<TripCrewAuthorizationOutcome> AuthorizeCrewForTripAsync(
        Guid tripId,
        Guid userId,
        Guid operatorId,
        string role,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Using dev Trip stub for AuthorizeCrewForTripAsync({TripId}, {UserId}, {Role}).",
            tripId,
            userId,
            role);
        return Task.FromResult(new TripCrewAuthorizationOutcome(
            TripCrewAuthorizationOutcomeKind.Authorized));
    }

    public Task<TripSnapshotOutcome> GetTripParcelSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for GetTripParcelSnapshotAsync({TripId}).", tripId);

        var now = DateTimeOffset.UtcNow;

        var snapshot = new TripParcelSnapshot(
            TripId: tripId,
            OperatorId: Guid.Parse("11111111-1111-4111-8111-111111111111"),
            RouteId: Guid.Parse("22222222-2222-4222-8222-222222222222"),
            VehicleId: Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Status: "SCHEDULED",
            DepartureDateTime: now.AddHours(4),
            EstimatedArrivalTime: now.AddHours(8),
            BaseFare: 200_000,
            OriginStation: new TripStationDto(
                Guid.Parse("44444444-4444-4444-8444-444444444444"),
                "Dev Origin"),
            DestinationStation: new TripStationDto(
                Guid.Parse("55555555-5555-4555-8555-555555555555"),
                "Dev Destination"),
            Stops: new[]
            {
                new TripStopDto(
                    Guid.Parse("66666666-6666-4666-8666-666666666666"),
                    1,
                    false,
                    true,
                    now.AddHours(6),
                    120,
                    null,
                    "ARRIVED",
                    now),
            },
            SeatSummary: new TripSeatSummaryDto(40, 40),
            ReturnRouteId: null,
            DestinationArrivedAt: now);

        return Task.FromResult(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success, snapshot, null));
    }

    public Task<TripOperationalLocationOutcome> GetTripOperationalLocationAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new TripOperationalLocationOutcome(
            TripOperationalLocationOutcomeKind.Success,
            new TripOperationalLocationSnapshot(
                tripId,
                Guid.Parse("33333333-3333-4333-8333-333333333333"),
                "IN_PROGRESS",
                Guid.Parse("66666666-6666-4666-8666-666666666666"),
                "ARRIVED",
                now,
                null,
                now),
            null));
    }

    public Task<TripSummaryBatchOutcome> GetTripSummariesAsync(
        IReadOnlyCollection<Guid> tripIds,
        CancellationToken cancellationToken = default)
    {
        if (tripIds.Any(tripId => tripId == Guid.Empty))
            throw new ArgumentException("Trip ids cannot contain an empty UUID.", nameof(tripIds));

        var distinctTripIds = tripIds.Distinct().ToArray();
        if (distinctTripIds.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(tripIds), "At most 100 distinct trip ids are allowed.");

        var now = DateTimeOffset.UtcNow;
        var summaries = distinctTripIds
            .Select(tripId => new TripSummarySnapshot(
                tripId,
                "SCHEDULED",
                now.AddHours(4),
                now.AddHours(8),
                new TripRouteSummarySnapshot(
                    Guid.Parse("22222222-2222-4222-8222-222222222222"),
                    "Dev Route",
                    "Dev Origin",
                    "Dev Destination"),
                new TripVehicleSummarySnapshot(
                    Guid.Parse("33333333-3333-4333-8333-333333333333"),
                    "DEV-0001",
                    "ACTIVE")))
            .ToArray();
        return Task.FromResult(TripSummaryBatchOutcome.Success(summaries));
    }

    public async Task<TripForwardingOptionsOutcome> GetForwardingOptionsAsync(
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
    {
        var tripId = Guid.Parse("77777777-7777-4777-8777-777777777777");
        var summary = (await GetTripSummariesAsync([tripId], cancellationToken)).Summaries.Single();
        return TripForwardingOptionsOutcome.Success(
        [
            new TripForwardingOptionSnapshot(
                summary,
                pickupLocationId,
                pickupLocationType,
                "Dev pickup",
                targetLocationId,
                targetLocationType,
                "Dev target",
                earliestDeparture.AddHours(1),
                earliestDeparture.AddHours(5),
                true,
                null),
        ]);
    }

    public Task<RouteOwnershipOutcome> ValidateRouteOwnershipAsync(
        Guid routeId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for ValidateRouteOwnershipAsync({RouteId}, {OperatorId}).", routeId, operatorId);
        return Task.FromResult(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));
    }

    public Task<RouteSearchOutcome> SearchRoutesAsync(
        Guid operatorId,
        string search,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for SearchRoutesAsync({OperatorId}).", operatorId);
        return Task.FromResult(RouteSearchOutcome.Success(
            [Guid.Parse("22222222-2222-4222-8222-222222222222")]));
    }

    public Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        decimal estimatedVolumeM3,
        ParcelSizeCategory sizeCategory,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for SearchAvailableParcelTripsAsync.");

        var now = DateTimeOffset.UtcNow;
        var operatorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var routeId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        var trips = new List<ParcelTripDto>
        {
            new(
                TripId: Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1"),
                RouteId: routeId,
                Status: "SCHEDULED",
                OperatorId: operatorId,
                OperatorName: "Dev Operator",
                OriginStation: new TripStationDto(originStationId, "Dev Origin"),
                DestinationStation: new TripStationDto(destinationStationId, "Dev Destination"),
                DepartureDateTime: now.AddHours(4),
                EstimatedArrivalTime: now.AddHours(8),
                AvailableCargoWeightKg: 50m,
                AvailableCargoVolumeM3: 3m,
                PriceVnd: 100_000),
            new(
                TripId: Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2"),
                RouteId: routeId,
                Status: "SCHEDULED",
                OperatorId: operatorId,
                OperatorName: "Dev Operator",
                OriginStation: new TripStationDto(originStationId, "Dev Origin"),
                DestinationStation: new TripStationDto(destinationStationId, "Dev Destination"),
                DepartureDateTime: now.AddHours(8),
                EstimatedArrivalTime: now.AddHours(12),
                AvailableCargoWeightKg: 30m,
                AvailableCargoVolumeM3: 2m,
                PriceVnd: 120_000),
        };

        return Task.FromResult(new ParcelTripSearchOutcome(
            ParcelTripSearchOutcomeKind.Success,
            trips,
            trips.Count,
            page,
            pageSize,
            null));
    }

    public Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        ParcelSizeCategory sizeCategory,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        => SearchAvailableParcelTripsAsync(
            originStationId,
            destinationStationId,
            departureDate,
            estimatedWeightKg,
            estimatedVolumeM3: 0.0001m,
            sizeCategory,
            page,
            pageSize,
            cancellationToken);

    public Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for ReserveCargoAsync({TripId}, {ParcelId}).", tripId, parcelId);
        return Task.FromResult(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
    }

    public Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
        => ReserveCargoAsync(tripId, parcelId, weightKg, volumeM3: 0.0001m, cancellationToken);

    public Task<TripCargoOutcome> ReserveCargoWithOverrideAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for ReserveCargoWithOverrideAsync({TripId}, {ParcelId}).", tripId, parcelId);
        return Task.FromResult(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
    }

    public Task<TripCargoOutcome> GetCargoCapacityAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for GetCargoCapacityAsync({TripId}).", tripId);
        return Task.FromResult(new TripCargoOutcome(
            TripCargoOutcomeKind.Success,
            null,
            new TripCargoCapacitySnapshot(
                tripId,
                ReservedWeightKg: 0m,
                ReservedVolumeM3: 0m,
                LoadedWeightKg: 0m,
                LoadedVolumeM3: 0m,
                MaxCargoWeightKg: 100m,
                MaxCargoVolumeM3: 5m,
                AvailableWeightKg: 100m,
                AvailableVolumeM3: 5m,
                PercentFull: 0m)));
    }

    public Task<TripCargoOutcome> RemeasureCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        bool allowCapacityOverflow = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for RemeasureCargoAsync({TripId}, {ParcelId}).", tripId, parcelId);
        return Task.FromResult(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
    }

    public Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for LoadCargoAsync({TripId}, {ParcelId}).", tripId, parcelId);
        return Task.FromResult(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
    }

    public Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
        => LoadCargoAsync(tripId, parcelId, weightKg, volumeM3: 0.0001m, cancellationToken);

    public Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for ReleaseCargoAsync({TripId}, {ParcelId}).", tripId, parcelId);
        return Task.FromResult(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
    }

    public Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
        => ReleaseCargoAsync(tripId, parcelId, weightKg, volumeM3: 0.0001m, cancellationToken);

    public Task<TripCargoTransferOutcome> TransferCargoAsync(
        Guid sourceTripId,
        Guid parcelId,
        Guid targetTripId,
        string targetState,
        bool allowCapacityOverflow,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Using dev Trip stub for TransferCargoAsync({SourceTripId}, {TargetTripId}, {ParcelId}).",
            sourceTripId,
            targetTripId,
            parcelId);
        return Task.FromResult(new TripCargoTransferOutcome(
            TripCargoTransferOutcomeKind.Success,
            Transfer: new TripCargoTransferSnapshot(
                parcelId,
                sourceTripId,
                targetTripId,
                targetState,
                1m,
                0.0001m)));
    }
}
