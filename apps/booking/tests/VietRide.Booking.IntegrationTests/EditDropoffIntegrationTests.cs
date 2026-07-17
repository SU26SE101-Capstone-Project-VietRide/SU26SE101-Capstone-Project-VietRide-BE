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
using VietRide.Booking.Application.Features.Bookings.EditDropoff;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

public sealed class EditDropoffIntegrationTests : IClassFixture<EditDropoffWebApplicationFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly EditDropoffWebApplicationFactory _factory;

    public EditDropoffIntegrationTests(EditDropoffWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostEditDropoff_ValidStop_Returns200AndUpdatesDropoff()
    {
        _factory.ResetCalls();
        var userId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var originStationId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var pickupStopId = Guid.NewGuid();
        var dropoffStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(userId, tripId, originStationId, pickupStopId, destinationStationId);
        var trip = CreateTripSnapshot(tripId, originStationId, destinationStationId, pickupStopId, dropoffStopId, allowDropoff: true);

        _factory.BookingRepository.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        var client = _factory.CreateAuthenticatedClient(userId);
        using var request = BuildRequest(booking.Id, new { dropoff = new { stopId = dropoffStopId } });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(200);
        var data = root.GetProperty("data");
        data.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
        data.GetProperty("dropoff").GetProperty("stationId").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("dropoff").GetProperty("stopId").GetGuid().Should().Be(dropoffStopId);
        data.GetProperty("fareDelta").GetInt64().Should().Be(0);

        booking.DropoffStationId.Should().BeNull();
        booking.DropoffStopId.Should().Be(dropoffStopId);
        _factory.BookingRepository.Received(1).Update(booking);
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .ChargeAsync(default!, default, default, default, default!, default!, default);
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .BatchChargeAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task PostEditDropoff_DisallowedStop_Returns422AndDoesNotUpdate()
    {
        _factory.ResetCalls();
        var userId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var originStationId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var pickupStopId = Guid.NewGuid();
        var dropoffStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(userId, tripId, originStationId, pickupStopId, destinationStationId);
        var trip = CreateTripSnapshot(tripId, originStationId, destinationStationId, pickupStopId, dropoffStopId, allowDropoff: false);

        _factory.BookingRepository.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        var client = _factory.CreateAuthenticatedClient(userId);
        using var request = BuildRequest(booking.Id, new { dropoff = new { stopId = dropoffStopId } });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(response, "STOP_NOT_DROPOFF_ALLOWED");
        booking.DropoffStationId.Should().Be(destinationStationId);
        booking.DropoffStopId.Should().BeNull();
        _factory.BookingRepository.DidNotReceiveWithAnyArgs().Update(default!);
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .ChargeAsync(default!, default, default, default, default!, default!, default);
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .BatchChargeAsync(default, default!, default!, default!, default);
    }

    [Theory]
    [MemberData(nameof(InvalidDropoffBodies))]
    public async Task PostEditDropoff_InvalidDropoffShape_Returns422AndDoesNotUpdate(object body)
    {
        _factory.ResetCalls();
        var userId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();

        var client = _factory.CreateAuthenticatedClient(userId);
        using var request = BuildRequest(bookingId, body);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
        _factory.BookingRepository.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task PostEditDropoff_DropoffStationNotTripDestination_Returns404AndDoesNotUpdate()
    {
        _factory.ResetCalls();
        var userId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var originStationId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var requestedStationId = Guid.NewGuid();
        var pickupStopId = Guid.NewGuid();
        var dropoffStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(userId, tripId, originStationId, pickupStopId, destinationStationId);
        var trip = CreateTripSnapshot(tripId, originStationId, destinationStationId, pickupStopId, dropoffStopId, allowDropoff: true);

        _factory.BookingRepository.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>()).Returns(trip);

        var client = _factory.CreateAuthenticatedClient(userId);
        using var request = BuildRequest(booking.Id, new { dropoff = new { stationId = requestedStationId } });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorCodeAsync(response, "STATION_NOT_FOUND");
        booking.DropoffStationId.Should().Be(destinationStationId);
        booking.DropoffStopId.Should().BeNull();
        _factory.BookingRepository.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task PostEditDropoff_NonOwner_Returns403()
    {
        _factory.ResetCalls();
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var originStationId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(ownerId, tripId, originStationId, Guid.NewGuid(), destinationStationId);

        _factory.BookingRepository.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var client = _factory.CreateAuthenticatedClient(callerId);
        using var request = BuildRequest(booking.Id, new { dropoff = new { stationId = destinationStationId } });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.TripClient.DidNotReceiveWithAnyArgs().GetTripSnapshotAsync(default, default);
    }

    public static TheoryData<object> InvalidDropoffBodies()
        => new()
        {
            new { },
            new { dropoff = (object?)null },
            new { dropoff = new { stationId = (Guid?)null, stopId = (Guid?)null } },
            new { dropoff = new { stationId = Guid.NewGuid(), stopId = Guid.NewGuid() } },
        };

    private static HttpRequestMessage BuildRequest(Guid bookingId, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/bookings/{bookingId}/edit-dropoff")
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

    private static BookingEntity CreateConfirmedBooking(
        Guid passengerUserId,
        Guid tripId,
        Guid originStationId,
        Guid pickupStopId,
        Guid destinationStationId)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(Now),
            passengerUserId: passengerUserId,
            tripId: tripId,
            operatorId: Guid.NewGuid(),
            pickupStationId: null,
            pickupStopId: pickupStopId,
            dropoffStationId: destinationStationId,
            dropoffStopId: null,
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000),
            tripSnapshotOriginName: "Hà Nội",
            tripSnapshotDestName: "Đà Nẵng",
            tripSnapshotDeparture: Now.AddHours(6),
            tripSnapshotRouteName: null);

        booking.Confirm(Now.AddMinutes(-10));
        return booking;
    }

    private static TripSnapshot CreateTripSnapshot(
        Guid tripId,
        Guid originStationId,
        Guid destinationStationId,
        Guid pickupStopId,
        Guid dropoffStopId,
        bool allowDropoff)
        => new(
            TripId: tripId,
            OperatorId: Guid.NewGuid(),
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: "SCHEDULED",
            DepartureDateTime: Now.AddHours(6),
            EstimatedArrivalTime: Now.AddHours(10),
            BaseFare: 200_000,
            OriginStation: new TripStationSnapshot(originStationId, "Hà Nội"),
            DestinationStation: new TripStationSnapshot(destinationStationId, "Đà Nẵng"),
            Stops:
            [
                new TripStopSnapshot(pickupStopId, 1, true, true, Now.AddHours(1), 42.5, 200_000),
                new TripStopSnapshot(dropoffStopId, 2, true, allowDropoff, Now.AddHours(2), 84.0, 200_000),
            ],
            SeatSummary: new TripSeatSummary(40, 38));
}

public sealed class EditDropoffWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);

    public ITripServiceClient TripClient { get; } = Substitute.For<ITripServiceClient>();
    public IPaymentServiceClient PaymentClient { get; } = Substitute.For<IPaymentServiceClient>();
    public IBookingRepository BookingRepository { get; } = Substitute.For<IBookingRepository>();

    public void ResetCalls()
    {
        TripClient.ClearReceivedCalls();
        PaymentClient.ClearReceivedCalls();
        BookingRepository.ClearReceivedCalls();
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

            var mockClock = Substitute.For<IClock>();
            mockClock.UtcNow.Returns(FixedNow);
            services.AddSingleton(mockClock);

            var mockUow = Substitute.For<IUnitOfWork>();
            mockUow.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<EditDropoffResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<Task<EditDropoffResult>>>()());
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
