using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;

namespace VietRide.Booking.IntegrationTests.Internal;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class Day24PendingPassengerCountEndpointTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory;

    public Day24PendingPassengerCountEndpointTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetPendingPassengerCount_ExecutesExactlyFiveBookingLocalPredicates()
    {
        await factory.InitializeAsync();
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await SeedBookingAsync(db, tripId, stopId, operatorId, BookingStatus.CONFIRMED,
                PassengerBoardingStatus.PENDING, PassengerBoardingStatus.PENDING,
                PassengerBoardingStatus.BOARDED);
            await SeedBookingAsync(db, tripId, stopId, operatorId, BookingStatus.PENDING_PAYMENT,
                PassengerBoardingStatus.PENDING);
            await SeedBookingAsync(db, Guid.NewGuid(), stopId, operatorId, BookingStatus.CONFIRMED,
                PassengerBoardingStatus.PENDING);
            await SeedBookingAsync(db, tripId, Guid.NewGuid(), operatorId, BookingStatus.CONFIRMED,
                PassengerBoardingStatus.PENDING);
            await SeedBookingAsync(db, tripId, stopId, Guid.NewGuid(), BookingStatus.CONFIRMED,
                PassengerBoardingStatus.PENDING);
            await SeedBookingAsync(db, tripId, stopId, operatorId, BookingStatus.CONFIRMED,
                PassengerBoardingStatus.NO_SHOW);
        }

        using var client = CreateInternalClient();
        var response = await client.GetAsync(BuildPath(tripId, stopId, operatorId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().Equal(
            "tripId",
            "stopId",
            "pendingPassengerCount");
        root.TryGetProperty("success", out _).Should().BeFalse();
        root.TryGetProperty("activeBookingCount", out _).Should().BeFalse();
        root.GetProperty("tripId").GetGuid().Should().Be(tripId);
        root.GetProperty("stopId").GetGuid().Should().Be(stopId);
        root.GetProperty("pendingPassengerCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetPendingPassengerCount_AbsentLogicalReferences_ReturnsRawZero()
    {
        await factory.InitializeAsync();
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        using var client = CreateInternalClient();

        var response = await client.GetAsync(BuildPath(tripId, stopId, operatorId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal(
            "tripId",
            "stopId",
            "pendingPassengerCount");
        document.RootElement.GetProperty("pendingPassengerCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetPendingPassengerCount_MalformedOrAllZeroEveryGuid_Returns422Envelope()
    {
        await factory.InitializeAsync();
        var valid = Guid.NewGuid().ToString("D");
        var zero = Guid.Empty.ToString("D");
        var cases = new[]
        {
            $"/internal/v1/bookings/trips/not-a-uuid/stops/{valid}/pending-passenger-count?operatorId={valid}",
            $"/internal/v1/bookings/trips/{zero}/stops/{valid}/pending-passenger-count?operatorId={valid}",
            $"/internal/v1/bookings/trips/{valid}/stops/not-a-uuid/pending-passenger-count?operatorId={valid}",
            $"/internal/v1/bookings/trips/{valid}/stops/{zero}/pending-passenger-count?operatorId={valid}",
            $"/internal/v1/bookings/trips/{valid}/stops/{valid}/pending-passenger-count?operatorId=not-a-uuid",
            $"/internal/v1/bookings/trips/{valid}/stops/{valid}/pending-passenger-count?operatorId={zero}",
            $"/internal/v1/bookings/trips/{valid}/stops/{valid}/pending-passenger-count",
        };
        using var client = CreateInternalClient();

        foreach (var path in cases)
        {
            var response = await client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, path);
            await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPendingPassengerCount_MissingOrInvalidInternalJwt_Returns401Envelope(
        bool addInvalidHeader)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        if (addInvalidHeader)
        {
            client.DefaultRequestHeaders.Add("X-Internal-Auth", "Bearer invalid-token");
        }

        var response = await client.GetAsync(
            BuildPath(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(response, "AUTH_TOKEN_INVALID");
    }

    [Fact]
    public async Task GetPendingPassengerCount_ObsoleteActiveByStopRoute_Returns404()
    {
        await factory.InitializeAsync();
        using var client = CreateInternalClient();

        var response = await client.GetAsync(
            $"/internal/v1/bookings/active-by-stop/{Guid.NewGuid():D}/count?operatorId={Guid.NewGuid():D}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient CreateInternalClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Auth", $"Bearer {MintInternalJwt()}");
        return client;
    }

    private static string BuildPath(Guid tripId, Guid stopId, Guid operatorId)
        => $"/internal/v1/bookings/trips/{tripId:D}/stops/{stopId:D}/pending-passenger-count?operatorId={operatorId:D}";

    private static async Task AssertErrorCodeAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(expectedCode);
    }

    private static async Task SeedBookingAsync(
        BookingDbContext db,
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        BookingStatus bookingStatus,
        params PassengerBoardingStatus[] passengerStatuses)
    {
        var bookingId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
        var bookingCode = $"VR-20260719-{Guid.NewGuid():N}"[..21].ToUpperInvariant();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.bookings
    (id, booking_code, passenger_user_id, trip_id, operator_id, pickup_stop_id,
     base_fare, discount_amount, total_amount, status, refund_override, created_at, updated_at)
VALUES
    ({bookingId}, {bookingCode}, {Guid.NewGuid()}, {tripId}, {operatorId}, {stopId},
     100000, 0, 100000, CAST({bookingStatus.ToString()} AS booking_status), FALSE, {now}, {now});");

        for (var index = 0; index < passengerStatuses.Length; index++)
        {
            var seatNumber = $"D24-{index}-{Guid.NewGuid():N}"[..20];
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.passengers
    (id, booking_id, seat_number, boarding_status, created_at, updated_at)
VALUES
    ({Guid.NewGuid()}, {bookingId}, {seatNumber},
     CAST({passengerStatuses[index].ToString()} AS passenger_boarding_status), {now}, {now});");
        }
    }

    private static string MintInternalJwt()
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["alg"] = "HS256",
                ["typ"] = "JWT",
            })));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["iss"] = "vietride-gateway",
                    ["aud"] = "vietride-internal",
                    ["sub"] = "trip-service",
                    ["jti"] = Guid.NewGuid().ToString("N"),
                    ["iat"] = now.ToUnixTimeSeconds(),
                    ["nbf"] = now.ToUnixTimeSeconds(),
                    ["exp"] = now.AddSeconds(120).ToUnixTimeSeconds(),
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var signature = Base64UrlEncode(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        return $"{signingInput}.{signature}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
