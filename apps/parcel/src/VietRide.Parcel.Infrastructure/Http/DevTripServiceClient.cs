using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

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
            Stops: Array.Empty<TripStopDto>(),
            SeatSummary: new TripSeatSummaryDto(40, 40),
            ReturnRouteId: null);

        return Task.FromResult(new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success, snapshot, null));
    }
}
