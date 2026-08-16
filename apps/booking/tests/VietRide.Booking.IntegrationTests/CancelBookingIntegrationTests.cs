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
using VietRide.Booking.Application.Features.Boarding.TickPassengerBoarded;
using VietRide.Booking.Application.Features.Bookings.CancelBooking;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

public sealed class CancelBookingIntegrationTests : IClassFixture<CancelBookingWebApplicationFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);

    private readonly CancelBookingWebApplicationFactory _factory;

    public CancelBookingIntegrationTests(CancelBookingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostCancel_ConfirmedBooking_Returns200CancelledEnvelope_AndReplaysIdempotently()
    {
        _factory.ResetCalls();
        var userId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var seatLockToken = Guid.NewGuid();
        var booking = CreateConfirmedBooking(userId, tripId, operatorId, seatLockToken);
        booking.AddPassenger("A01");

        _factory.BookingRepository.FindByIdWithPassengersAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _factory.BookingRepository.TryCancelAsync(
                booking.Id,
                BookingCancellationReason.USER_INITIATED,
                Now,
                false,
                Arg.Any<CancellationToken>())
            .Returns(true);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(tripId, operatorId, "SCHEDULED"));
        _factory.OperatorClient.GetOperatorAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(CreateOperatorLookup(operatorId));

        var client = _factory.CreateAuthenticatedClient(userId);
        var idempotencyKey = Guid.NewGuid().ToString();
        using var first = BuildRequest(booking.Id, idempotencyKey);
        using var second = BuildRequest(booking.Id, idempotencyKey);

        var firstResponse = await client.SendAsync(first);
        var firstJson = await firstResponse.Content.ReadAsStringAsync();
        var secondResponse = await client.SendAsync(second);
        var secondJson = await secondResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondJson.Should().Be(firstJson, "same Idempotency-Key and body must replay the cached response");

        using var doc = JsonDocument.Parse(firstJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(200);
        var data = root.GetProperty("data");
        data.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
        data.GetProperty("status").GetString().Should().Be("CANCELLED");
        data.GetProperty("refundAmount").GetInt64().Should().Be(180_000);
        data.GetProperty("refundMethod").GetString().Should().Be("WALLET");

        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .ReleaseSeatsAsync(default, default, default!, default);
        await _factory.Outbox.Received(1).EnqueueAsync(
            Arg.Is<Guid>(eventId => eventId != Guid.Empty),
            "booking.booking.cancelled",
            Arg.Is<string>(json => EventHasPassengerAndRefund(json, userId, 180_000)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostCancel_ExpiredBooking_Returns409BookingNotCancellable()
    {
        _factory.ResetCalls();
        var userId = Guid.NewGuid();
        var booking = CreateExpiredBooking(userId);
        _factory.BookingRepository.FindByIdWithPassengersAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var client = _factory.CreateAuthenticatedClient(userId);
        using var request = BuildRequest(booking.Id, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertErrorCodeAsync(response, "BOOKING_NOT_CANCELLABLE");
        await _factory.TripClient.DidNotReceiveWithAnyArgs().GetTripSnapshotAsync(default, default);
        await _factory.Outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default!, default);
    }

    private static HttpRequestMessage BuildRequest(Guid bookingId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/bookings/{bookingId}/cancel")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { reason = "USER_INITIATED" }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static bool EventHasPassengerAndRefund(string json, Guid userId, long refundAmount)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return root.GetProperty("userId").GetGuid() == userId
            && root.GetProperty("eventId").GetGuid() != Guid.Empty
            && root.GetProperty("occurredAt").GetDateTimeOffset() == Now
            && root.GetProperty("refundAmount").GetInt64() == refundAmount
            && root.GetProperty("refundOverride").GetBoolean() == false
            && root.GetProperty("cancellationReason").GetString() == "USER_INITIATED";
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedCode);
    }

    private static BookingEntity CreateConfirmedBooking(Guid passengerUserId, Guid tripId, Guid operatorId, Guid seatLockToken)
    {
        var booking = CreatePendingBooking(passengerUserId, tripId, operatorId, seatLockToken);
        booking.Confirm(Now.AddMinutes(-10));
        return booking;
    }

    private static BookingEntity CreateExpiredBooking(Guid passengerUserId)
    {
        var booking = CreatePendingBooking(passengerUserId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        booking.ExpirePayment(Now.AddMinutes(-10));
        return booking;
    }

    private static BookingEntity CreatePendingBooking(Guid passengerUserId, Guid tripId, Guid operatorId, Guid seatLockToken)
        => BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(Now),
            passengerUserId: passengerUserId,
            tripId: tripId,
            operatorId: operatorId,
            pickupStationId: Guid.NewGuid(),
            pickupStopId: null,
            dropoffStationId: null,
            dropoffStopId: null,
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000),
            tripSnapshotOriginName: "Ha Noi",
            tripSnapshotDestName: "Da Nang",
            tripSnapshotDeparture: Now.AddHours(25),
            tripSnapshotRouteName: null,
            seatLockToken: seatLockToken);

    private static TripSnapshot CreateTripSnapshot(Guid tripId, Guid operatorId, string status)
        => new(
            TripId: tripId,
            OperatorId: operatorId,
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: status,
            DepartureDateTime: Now.AddHours(24),
            EstimatedArrivalTime: Now.AddHours(28),
            BaseFare: 200_000,
            OriginStation: new TripStationSnapshot(Guid.NewGuid(), "Ha Noi"),
            DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "Da Nang"),
            Stops: [],
            SeatSummary: new TripSeatSummary(40, 38));

    private static OperatorLookup CreateOperatorLookup(Guid operatorId)
        => new(
            operatorId,
            "VietRide Limousine",
            "APPROVED",
            true,
            "ops@example.com",
            "+84901234567",
            "0312345678",
            "0312345678",
            JsonSerializer.SerializeToElement(new[]
            {
                new { hoursBeforeDeparture = 24, feePercent = 10 },
            }));
}

public sealed class CancelBookingWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);

    public ITripServiceClient TripClient { get; } = Substitute.For<ITripServiceClient>();
    public IOperatorServiceClient OperatorClient { get; } = Substitute.For<IOperatorServiceClient>();
    public IBookingRepository BookingRepository { get; } = Substitute.For<IBookingRepository>();
    public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();

    public void ResetCalls()
    {
        TripClient.ClearReceivedCalls();
        OperatorClient.ClearReceivedCalls();
        BookingRepository.ClearReceivedCalls();
        Outbox.ClearReceivedCalls();
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
            services.AddSingleton(OperatorClient);
            services.AddSingleton(BookingRepository);
            services.AddSingleton(Outbox);

            var mockClock = Substitute.For<IClock>();
            mockClock.UtcNow.Returns(FixedNow);
            services.AddSingleton(mockClock);

            var mockUow = Substitute.For<IUnitOfWork>();
            mockUow.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<CancelBookingResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<Task<CancelBookingResult>>>()());
            mockUow.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<TickPassengerBoardedResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<Task<TickPassengerBoardedResult>>>()());
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
