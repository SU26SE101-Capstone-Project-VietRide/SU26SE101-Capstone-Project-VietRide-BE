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
            ReturnRouteId: null);

        return Task.FromResult(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success, snapshot, null));
    }

    public Task<RouteOwnershipOutcome> ValidateRouteOwnershipAsync(
        Guid routeId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for ValidateRouteOwnershipAsync({RouteId}, {OperatorId}).", routeId, operatorId);
        return Task.FromResult(new RouteOwnershipOutcome(RouteOwnershipOutcomeKind.Success, null));
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
                OperatorId: operatorId,
                OperatorName: "Dev Operator",
                DepartureDateTime: now.AddHours(4),
                AvailableCargoWeightKg: 50m,
                PriceVnd: 100_000),
            new(
                TripId: Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2"),
                RouteId: routeId,
                OperatorId: operatorId,
                OperatorName: "Dev Operator",
                DepartureDateTime: now.AddHours(8),
                AvailableCargoWeightKg: 30m,
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

    public Task<TripCargoOutcome> ReserveCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for ReserveCargoAsync({TripId}, {ParcelId}).", tripId, parcelId);
        return Task.FromResult(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
    }

    public Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for LoadCargoAsync({TripId}, {ParcelId}).", tripId, parcelId);
        return Task.FromResult(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
    }

    public Task<TripCargoOutcome> ReleaseCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Using dev Trip stub for ReleaseCargoAsync({TripId}, {ParcelId}).", tripId, parcelId);
        return Task.FromResult(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
    }
}
