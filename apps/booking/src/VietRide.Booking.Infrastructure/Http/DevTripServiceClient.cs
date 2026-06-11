using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Infrastructure.Http;

/// <summary>
/// Day-12 development stub for the Trip seat-lock seam.
/// Keeps Booking E2E runnable before the Trip-owned TripSeat/Redis implementation lands.
/// </summary>
public sealed class DevTripServiceClient : ITripServiceClient
{
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

        var snapshot = new TripSnapshot(
            TripId: tripId,
            OperatorId: Guid.Parse("11111111-1111-4111-8111-111111111111"),
            RouteId: Guid.Parse("22222222-2222-4222-8222-222222222222"),
            VehicleId: Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Status: "SCHEDULED",
            DepartureDateTime: now.AddHours(2),
            EstimatedArrivalTime: now.AddHours(6),
            BaseFare: 200_000,
            OriginStation: new TripStationSnapshot(
                Guid.Parse("44444444-4444-4444-8444-444444444444"),
                "Day-12 Dev Origin"),
            DestinationStation: new TripStationSnapshot(
                Guid.Parse("55555555-5555-4555-8555-555555555555"),
                "Day-12 Dev Destination"),
            Stops: [],
            SeatSummary: new TripSeatSummary(40, 40));

        return Task.FromResult<TripSnapshot?>(snapshot);
    }

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
