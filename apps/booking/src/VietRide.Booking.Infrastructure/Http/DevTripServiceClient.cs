using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Infrastructure.Http;

/// <summary>
/// Day-12 development stub for the Trip seat-lock seam.
/// Keeps Booking E2E runnable before the Trip-owned TripSeat/Redis implementation lands.
/// </summary>
public sealed class DevTripServiceClient : ITripServiceClient
{
    public static readonly Guid TripWithoutReturnRouteId = Guid.Parse("00000000-0000-4000-8000-000000000013");
    public static readonly Guid RoundTripReturnTripId = Guid.Parse("00000000-0000-4000-8000-000000000113");

    private readonly ILogger<DevTripServiceClient> _logger;

    public DevTripServiceClient(ILogger<DevTripServiceClient> logger)
    {
        _logger = logger;
    }

    public Task<TripSnapshot?> GetTripSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var routeId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var returnRouteId = tripId == TripWithoutReturnRouteId
            ? (Guid?)null
            : CreateDeterministicGuid($"return-route:{routeId}");

        var isReturnTrip = tripId == RoundTripReturnTripId;
        var departureDateTime = isReturnTrip ? now.AddHours(10) : now.AddHours(4);
        var estimatedArrivalTime = isReturnTrip ? now.AddHours(14) : now.AddHours(8);

        var snapshot = new TripSnapshot(
            TripId: tripId,
            OperatorId: Guid.Parse("11111111-1111-4111-8111-111111111111"),
            RouteId: routeId,
            VehicleId: Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Status: "SCHEDULED",
            DepartureDateTime: departureDateTime,
            EstimatedArrivalTime: estimatedArrivalTime,
            BaseFare: 200_000,
            OriginStation: new TripStationSnapshot(
                Guid.Parse("44444444-4444-4444-8444-444444444444"),
                "Day-12 Dev Origin"),
            DestinationStation: new TripStationSnapshot(
                Guid.Parse("55555555-5555-4555-8555-555555555555"),
                "Day-12 Dev Destination"),
            Stops: [],
            SeatSummary: new TripSeatSummary(40, 40),
            ReturnRouteId: returnRouteId);

        return Task.FromResult<TripSnapshot?>(snapshot);
    }

    public Task<ShuttleRoadDistanceOutcome> GetShuttleRoadDistanceAsync(
        Guid tripId,
        string direction,
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken = default)
        => Task.FromResult<ShuttleRoadDistanceOutcome>(new ShuttleRoadDistanceOutcome.Success(1_000));

    public Task<TripSnapshot?> GetTripSnapshotAsync(
        Guid tripId,
        DateTimeOffset pricingAt,
        CancellationToken cancellationToken)
        => GetTripSnapshotAsync(tripId, cancellationToken);

    public Task<LockSeatsOutcome> LockSeatsAsync(
        Guid tripId,
        IReadOnlyList<string> seatNumbers,
        Guid holdOwnerId,
        string idempotencyKey,
        int? ttlSeconds = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Using Day-12 dev Trip stub to lock seats {SeatNumbers} for trip {TripId}.",
            string.Join(",", seatNumbers),
            tripId);

        var token = CreateDeterministicGuid($"{tripId}:{idempotencyKey}:{string.Join(',', seatNumbers)}");
        var result = new SeatLockResult(
            token,
            seatNumbers.ToArray(),
            DateTimeOffset.UtcNow.AddSeconds(ttlSeconds ?? 600));

        return Task.FromResult<LockSeatsOutcome>(new LockSeatsOutcome.Success(result));
    }

    public Task<LockRoundTripSeatsOutcome> LockRoundTripSeatsAsync(
        Guid outboundTripId,
        IReadOnlyList<string> outboundSeatNumbers,
        Guid returnTripId,
        IReadOnlyList<string> returnSeatNumbers,
        Guid holdOwnerId,
        string idempotencyKey,
        int? ttlSeconds = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Using Day-12 dev Trip stub to atomically lock round-trip seats outbound {OutboundSeats} and return {ReturnSeats}.",
            string.Join(",", outboundSeatNumbers),
            string.Join(",", returnSeatNumbers));

        var outboundToken = CreateDeterministicGuid($"{outboundTripId}:{idempotencyKey}:outbound:{string.Join(',', outboundSeatNumbers)}");
        var returnToken = CreateDeterministicGuid($"{returnTripId}:{idempotencyKey}:return:{string.Join(',', returnSeatNumbers)}");

        var outbound = new RoundTripSeatLockResult(
            outboundTripId,
            outboundToken,
            outboundSeatNumbers.ToArray(),
            DateTimeOffset.UtcNow.AddSeconds(ttlSeconds ?? 600));

        var @return = new RoundTripSeatLockResult(
            returnTripId,
            returnToken,
            returnSeatNumbers.ToArray(),
            DateTimeOffset.UtcNow.AddSeconds(ttlSeconds ?? 600));

        return Task.FromResult<LockRoundTripSeatsOutcome>(new LockRoundTripSeatsOutcome.Success(outbound, @return));
    }

    public Task<bool> BookSeatsAsync(
        Guid tripId,
        Guid seatLockToken,
        Guid bookingId,
        IReadOnlyList<PassengerSeatAssignment> passengerSeatAssignments,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Using Day-12 dev Trip stub to book {SeatCount} seats for booking {BookingId}.",
            passengerSeatAssignments.Count,
            bookingId);

        return Task.FromResult(true);
    }

    public Task<bool> BookRoundTripSeatsAsync(
        RoundTripBookSeatsLeg outbound,
        RoundTripBookSeatsLeg @return,
        CancellationToken cancellationToken = default,
        Guid? operationId = null)
        => Task.FromResult(true);

    public Task ReleaseSeatsAsync(
        Guid tripId,
        Guid seatLockToken,
        IReadOnlyList<string> seatNumbers,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Using Day-12 dev Trip stub to release seats {SeatNumbers} for trip {TripId}.",
            string.Join(",", seatNumbers),
            tripId);

        return Task.CompletedTask;
    }

    private static Guid CreateDeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes[..16]);
    }
}
