using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.EditPickup;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

public sealed class EditPickupIntegrationTests : IClassFixture<EditPickupWebApplicationFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly EditPickupWebApplicationFactory _factory;

    public EditPickupIntegrationTests(EditPickupWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostEditPickup_EqualFare_Returns200AndUpdatesPickup()
    {
        _factory.ResetCalls();
        var userId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var newStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(userId, tripId, stationId, baseFare: 200_000);
        var trip = CreateTripSnapshot(tripId, stationId, newStopId, baseFare: 200_000, stopFare: 200_000);

        _factory.BookingRepository.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        var client = _factory.CreateAuthenticatedClient(userId);
        using var request = BuildRequest(booking.Id, new { pickup = new { stopId = newStopId }, paymentMethod = "WALLET" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(200);
        var data = root.GetProperty("data");
        data.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
        data.GetProperty("pickup").GetProperty("stationId").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("pickup").GetProperty("stopId").GetGuid().Should().Be(newStopId);
        data.GetProperty("fareDelta").GetInt64().Should().Be(0);
        data.GetProperty("refundAmount").GetInt64().Should().Be(0);
        data.GetProperty("paymentRedirectUrl").ValueKind.Should().Be(JsonValueKind.Null);

        booking.PickupStationId.Should().BeNull();
        booking.PickupStopId.Should().Be(newStopId);
        _factory.BookingRepository.Received(1).Update(booking);
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .ChargeAsync(default!, default, default, default, default!, default!, default);
    }

    [Fact]
    public async Task PostEditPickup_HigherFare_Returns409PriceChanged()
    {
        _factory.ResetCalls();
        var userId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var newStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(userId, tripId, stationId, baseFare: 200_000);
        var trip = CreateTripSnapshot(tripId, stationId, newStopId, baseFare: 200_000, stopFare: 250_000);

        _factory.BookingRepository.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        var client = _factory.CreateAuthenticatedClient(userId);
        using var request = BuildRequest(booking.Id, new { pickup = new { stopId = newStopId }, paymentMethod = "WALLET" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertErrorCodeAsync(response, "BOOKING_EDIT_PICKUP_PRICE_CHANGED");
        _factory.BookingRepository.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task PostEditPickup_DisallowedStop_Returns422AndDoesNotUpdate()
    {
        _factory.ResetCalls();
        var userId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var newStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(userId, tripId, stationId, baseFare: 200_000);
        var trip = CreateTripSnapshot(tripId, stationId, newStopId, baseFare: 200_000, stopFare: 200_000, allowPickup: false);

        _factory.BookingRepository.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        var client = _factory.CreateAuthenticatedClient(userId);
        using var request = BuildRequest(booking.Id, new { pickup = new { stopId = newStopId }, paymentMethod = "WALLET" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(response, "STOP_NOT_PICKUP_ALLOWED");
        booking.PickupStationId.Should().Be(stationId);
        booking.PickupStopId.Should().BeNull();
        _factory.BookingRepository.DidNotReceiveWithAnyArgs().Update(default!);
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .ChargeAsync(default!, default, default, default, default!, default!, default);
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .BatchChargeAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task PostEditPickup_NonOwner_Returns403()
    {
        _factory.ResetCalls();
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(ownerId, tripId, stationId, baseFare: 200_000);

        _factory.BookingRepository.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var client = _factory.CreateAuthenticatedClient(callerId);
        using var request = BuildRequest(booking.Id, new { pickup = new { stationId }, paymentMethod = "WALLET" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.TripClient.DidNotReceiveWithAnyArgs().GetTripSnapshotAsync(default, default);
    }

    [Fact]
    public async Task PostEditPickup_UnknownBooking_Returns404BookingNotFound()
    {
        _factory.ResetCalls();
        var bookingId = Guid.NewGuid();
        _factory.BookingRepository.FindByIdAsync(bookingId, Arg.Any<CancellationToken>()).Returns((BookingEntity?)null);

        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var request = BuildRequest(bookingId, new { pickup = new { stationId = Guid.NewGuid() }, paymentMethod = "WALLET" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorCodeAsync(response, "BOOKING_NOT_FOUND");
    }

    private static HttpRequestMessage BuildRequest(Guid bookingId, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/bookings/{bookingId}/edit-pickup")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedCode);
    }

    private static BookingEntity CreateConfirmedBooking(Guid passengerUserId, Guid tripId, Guid stationId, long baseFare)
    {
        var now = Now;
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(now),
            passengerUserId: passengerUserId,
            tripId: tripId,
            operatorId: Guid.NewGuid(),
            pickupStationId: stationId,
            pickupStopId: null,
            dropoffStationId: null,
            dropoffStopId: null,
            baseFare: Money.FromRaw(baseFare),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(baseFare),
            tripSnapshotOriginName: "Hà Nội",
            tripSnapshotDestName: "Đà Nẵng",
            tripSnapshotDeparture: Now.AddHours(6),
            tripSnapshotRouteName: null);

        booking.Confirm(Now.AddMinutes(-10));
        return booking;
    }

    private static TripSnapshot CreateTripSnapshot(Guid tripId, Guid stationId, Guid stopId, long baseFare, long? stopFare, bool allowPickup = true)
        => new(
            TripId: tripId,
            OperatorId: Guid.NewGuid(),
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: "SCHEDULED",
            DepartureDateTime: Now.AddHours(6),
            EstimatedArrivalTime: Now.AddHours(10),
            BaseFare: baseFare,
            OriginStation: new TripStationSnapshot(stationId, "Hà Nội"),
            DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "Đà Nẵng"),
            Stops: [new TripStopSnapshot(stopId, 1, allowPickup, true, Now.AddHours(1), 42.5, stopFare)],
            SeatSummary: new TripSeatSummary(40, 38));
}

public sealed class EditPickupWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);

    public ITripServiceClient TripClient { get; } = Substitute.For<ITripServiceClient>();
    public IPaymentServiceClient PaymentClient { get; } = Substitute.For<IPaymentServiceClient>();
    public IBookingRepository BookingRepository { get; } = Substitute.For<IBookingRepository>();
    public IBookingPendingActionRepository PendingActionRepository { get; } = Substitute.For<IBookingPendingActionRepository>();

    public void ResetCalls()
    {
        TripClient.ClearReceivedCalls();
        PaymentClient.ClearReceivedCalls();
        BookingRepository.ClearReceivedCalls();
        PendingActionRepository.ClearReceivedCalls();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(TripClient);
            services.AddSingleton(PaymentClient);
            services.AddSingleton<IBookingStationCanonicalizer>(
                PassthroughBookingStationCanonicalizer.Instance);
            BookingRepository.FindByIdForUpdateAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => BookingRepository.FindByIdAsync(
                    call.Arg<Guid>(),
                    call.Arg<CancellationToken>()));
            services.AddSingleton(BookingRepository);
            services.AddSingleton(PendingActionRepository);

            var mockClock = Substitute.For<IClock>();
            mockClock.UtcNow.Returns(FixedNow);
            services.AddSingleton(mockClock);

            var mockUow = Substitute.For<IUnitOfWork>();
            mockUow.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<EditPickupResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<Task<EditPickupResult>>>()());
            services.AddSingleton(mockUow);

            services.AddSingleton<IConnectionMultiplexer>(InMemoryIdempotencyRedis.Create());
        });
    }

    public HttpClient CreateAuthenticatedClient(Guid userId, string role = "PASSENGER")
    {
        var client = CreateClient();
        var token = MintInternalJwt(userId.ToString(), role);
        client.DefaultRequestHeaders.Add("X-Internal-Auth", $"Bearer {token}");
        return client;
    }

    private static string MintInternalJwt(string subject, string role)
    {
        var secretBytes = Encoding.UTF8.GetBytes(TestSecret);
        var now = DateTimeOffset.UtcNow;

        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["alg"] = "HS256",
                ["typ"] = "JWT",
            })));

        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["iss"] = "vietride-gateway",
                ["aud"] = "vietride-internal",
                ["sub"] = subject,
                ["role"] = role,
                ["jti"] = Guid.NewGuid().ToString("N"),
                ["iat"] = now.ToUnixTimeSeconds(),
                ["nbf"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddSeconds(120).ToUnixTimeSeconds(),
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(secretBytes);
        var sig = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        return $"{signingInput}.{sig}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
