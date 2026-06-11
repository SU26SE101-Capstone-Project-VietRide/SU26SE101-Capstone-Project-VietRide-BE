using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Infrastructure.Http;

namespace VietRide.Booking.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="TripServiceClient"/> covering the four Trip seam methods.
/// Uses a <see cref="FakeMessageHandler"/> to stub HTTP responses without a real Trip service.
/// </summary>
public class TripServiceClientTests
{
    private static readonly Guid TripId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LockToken = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BookingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid PassengerId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------------
    // GetTripSnapshotAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetTripSnapshotAsync_Returns_Snapshot_On_200()
    {
        var snapshot = BuildTripSnapshotJson();
        var client = BuildClient(HttpStatusCode.OK, snapshot);

        var result = await client.GetTripSnapshotAsync(TripId);

        result.Should().NotBeNull();
        result!.TripId.Should().Be(TripId);
        result.Status.Should().Be("SCHEDULED");
        result.BaseFare.Should().Be(400_000);
    }

    [Fact]
    public async Task GetTripSnapshotAsync_Returns_Null_On_404()
    {
        var client = BuildClient(HttpStatusCode.NotFound, "{}");

        var result = await client.GetTripSnapshotAsync(TripId);

        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // LockSeatsAsync — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LockSeatsAsync_Returns_Success_On_200()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var body = JsonSerializer.Serialize(new
        {
            success = true,
            statusCode = 200,
            data = new
            {
                seatLockToken = LockToken,
                lockedSeats = new[] { "A01", "A02" },
                expiresAt = expiresAt,
            },
            meta = new { traceId = "t1" },
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.LockSeatsAsync(
            TripId, ["A01", "A02"], UserId, idempotencyKey: "idem-1");

        result.Should().BeOfType<LockSeatsOutcome.Success>();
        var ok = (LockSeatsOutcome.Success)result;
        ok.Data.SeatLockToken.Should().Be(LockToken);
        ok.Data.LockedSeats.Should().BeEquivalentTo(["A01", "A02"]);
    }

    // -----------------------------------------------------------------------
    // LockSeatsAsync — error cases
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LockSeatsAsync_Returns_TripNotFound_On_404()
    {
        var client = BuildClient(HttpStatusCode.NotFound, ErrorBody("TRIP_NOT_FOUND", "not found"));

        var result = await client.LockSeatsAsync(
            TripId, ["A01"], UserId, idempotencyKey: "idem-2");

        result.Should().BeOfType<LockSeatsOutcome.TripNotFound>();
    }

    [Fact]
    public async Task LockSeatsAsync_Returns_SeatUnavailable_On_409_BOOKING_SEAT_UNAVAILABLE()
    {
        var body = JsonSerializer.Serialize(new
        {
            success = false,
            statusCode = 409,
            error = new
            {
                code = "BOOKING_SEAT_UNAVAILABLE",
                message = "Seat A01 is held.",
                fields = new[] { new { field = "seatNumbers", value = new[] { "A01" } } },
            },
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.Conflict, body);

        var result = await client.LockSeatsAsync(
            TripId, ["A01"], UserId, idempotencyKey: "idem-3");

        result.Should().BeOfType<LockSeatsOutcome.SeatUnavailable>();
    }

    [Fact]
    public async Task LockSeatsAsync_Returns_TripNotBookable_On_409_BOOKING_TRIP_NOT_BOOKABLE()
    {
        var body = ErrorBody("BOOKING_TRIP_NOT_BOOKABLE", "Trip is not scheduled.");
        var client = BuildClient(HttpStatusCode.Conflict, body);

        var result = await client.LockSeatsAsync(
            TripId, ["A01"], UserId, idempotencyKey: "idem-4");

        result.Should().BeOfType<LockSeatsOutcome.TripNotBookable>();
    }

    // -----------------------------------------------------------------------
    // BookSeatsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BookSeatsAsync_Returns_True_On_204()
    {
        var client = BuildClient(HttpStatusCode.NoContent, string.Empty);

        var result = await client.BookSeatsAsync(
            TripId, LockToken, BookingId,
            [new PassengerSeatAssignment(PassengerId, "A01")]);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task BookSeatsAsync_Returns_False_On_409_Lock_Expired()
    {
        var client = BuildClient(HttpStatusCode.Conflict,
            ErrorBody("BOOKING_SEAT_UNAVAILABLE", "Lock expired."));

        var result = await client.BookSeatsAsync(
            TripId, LockToken, BookingId,
            [new PassengerSeatAssignment(PassengerId, "A01")]);

        result.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ReleaseSeatsAsync — idempotent, does not throw on 404 / 409
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReleaseSeatsAsync_DoesNotThrow_On_204()
    {
        var client = BuildClient(HttpStatusCode.NoContent, string.Empty);

        var act = async () => await client.ReleaseSeatsAsync(TripId, LockToken, ["A01"]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReleaseSeatsAsync_DoesNotThrow_On_404_Already_Released()
    {
        var client = BuildClient(HttpStatusCode.NotFound,
            ErrorBody("TRIP_NOT_FOUND", "not found"));

        var act = async () => await client.ReleaseSeatsAsync(TripId, LockToken, ["A01"]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReleaseSeatsAsync_DoesNotThrow_On_409_Lock_Already_Expired()
    {
        // 409 means the lock was already released or expired — idempotent no-op per seam contract.
        var client = BuildClient(HttpStatusCode.Conflict,
            ErrorBody("BOOKING_SEAT_UNAVAILABLE", "Lock already expired."));

        var act = async () => await client.ReleaseSeatsAsync(TripId, LockToken, ["A01"]);

        await act.Should().NotThrowAsync();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TripServiceClient BuildClient(HttpStatusCode status, string body)
    {
        var handler = new FakeMessageHandler(status, body);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://trip-service"),
        };
        return new TripServiceClient(httpClient, NullLogger<TripServiceClient>.Instance);
    }

    private static string BuildTripSnapshotJson() => JsonSerializer.Serialize(new
    {
        tripId = TripId,
        operatorId = Guid.NewGuid(),
        routeId = Guid.NewGuid(),
        vehicleId = Guid.NewGuid(),
        status = "SCHEDULED",
        departureDateTime = DateTimeOffset.UtcNow.AddDays(1),
        estimatedArrivalTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(12),
        baseFare = 400_000,
        originStation = new { id = Guid.NewGuid(), name = "Bến xe Miền Đông" },
        destinationStation = new { id = Guid.NewGuid(), name = "Bến xe Mỹ Đình" },
        stops = Array.Empty<object>(),
        seatSummary = new { totalSeats = 40, availableSeats = 18 },
    }, JsonOptions);

    private static string ErrorBody(string code, string message) =>
        JsonSerializer.Serialize(new
        {
            success = false,
            statusCode = 409,
            error = new { code, message },
        }, JsonOptions);

    // -----------------------------------------------------------------------
    // Fake HTTP handler
    // -----------------------------------------------------------------------

    private sealed class FakeMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeMessageHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
