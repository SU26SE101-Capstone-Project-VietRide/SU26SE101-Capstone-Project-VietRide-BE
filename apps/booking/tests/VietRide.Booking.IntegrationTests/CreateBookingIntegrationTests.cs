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
using VietRide.Booking.Application.Features.Bookings.CreateBooking;
using VietRide.Shared.Application.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

/// <summary>
/// Integration tests for POST /v1/bookings (Task 12.3 — B2 requirement).
/// Tests run through the real HTTP pipeline (WebApplicationFactory) with
/// ITripServiceClient + IPaymentServiceClient replaced by NSubstitute mocks
/// via ConfigureTestServices.
/// </summary>
public class CreateBookingIntegrationTests
    : IClassFixture<CreateBookingWebApplicationFactory>
{
    private readonly CreateBookingWebApplicationFactory _factory;

    public CreateBookingIntegrationTests(CreateBookingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // -----------------------------------------------------------------------
    // Test 1 (B2 requirement): WALLET happy path → 201 CONFIRMED envelope
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostBookings_WalletHappyPath_Returns201ConfirmedEnvelope()
    {
        // Arrange: clear any call history from previous tests in this shared fixture
        _factory.TripClient.ClearReceivedCalls();
        _factory.PaymentClient.ClearReceivedCalls();
        _factory.BookingRepository.ClearReceivedCalls();

        var tripId = Guid.NewGuid();
        var lockToken = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        var tripSnapshot = new TripSnapshot(
            TripId: tripId,
            OperatorId: operatorId,
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: "SCHEDULED",
            DepartureDateTime: DateTimeOffset.UtcNow.AddHours(2),
            EstimatedArrivalTime: DateTimeOffset.UtcNow.AddHours(4),
            BaseFare: 200_000,
            OriginStation: new TripStationSnapshot(stationId, "Hà Nội"),
            DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "TP.HCM"),
            Stops: [],
            SeatSummary: new TripSeatSummary(40, 38));

        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(tripSnapshot);
        _factory.TripClient.LockSeatsAsync(
                tripId,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new LockSeatsOutcome.Success(
                new SeatLockResult(lockToken, ["A01"], DateTimeOffset.UtcNow.AddMinutes(10))));
        _factory.PaymentClient.ChargeAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<PaymentContextSnapshot?>())
            .Returns(new ChargeOutcome.Success(
                new ChargeResult(Guid.NewGuid(), "SUCCEEDED", null)));
        _factory.TripClient.BookSeatsAsync(
                tripId,
                lockToken,
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        var userId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(userId);
        var idempotencyKey = Guid.NewGuid().ToString();

        var body = JsonSerializer.Serialize(new
        {
            tripId,
            pickup = new { stationId },
            seats = new[]
            {
                new
                {
                    seatNumber = "A01",
                    passenger = new { fullName = "Nguyen Van A", phoneNumber = "0900000001", idNumber = "012345678901" },
                },
            },
            paymentMethod = "WALLET",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/bookings")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(201);
        root.GetProperty("data").GetProperty("status").GetString()
            .Should().Be("CONFIRMED");
        root.GetProperty("data").GetProperty("totalAmount").GetInt64()
            .Should().Be(200_000);
        root.GetProperty("meta").GetProperty("traceId").GetString()
            .Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // Test 2 (B2 requirement): lock failure → 409 AND no booking persisted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostBookings_SeatUnavailable_Returns409_AndNoBookingPersisted()
    {
        // Arrange — clear previous test's mock call history so DidNotReceive assertions are clean
        _factory.TripClient.ClearReceivedCalls();
        _factory.PaymentClient.ClearReceivedCalls();
        _factory.BookingRepository.ClearReceivedCalls();

        var tripId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        var tripSnapshot = new TripSnapshot(
            TripId: tripId,
            OperatorId: Guid.NewGuid(),
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: "SCHEDULED",
            DepartureDateTime: DateTimeOffset.UtcNow.AddHours(2),
            EstimatedArrivalTime: DateTimeOffset.UtcNow.AddHours(4),
            BaseFare: 150_000,
            OriginStation: new TripStationSnapshot(stationId, "Hà Nội"),
            DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "TP.HCM"),
            Stops: [],
            SeatSummary: new TripSeatSummary(40, 0)); // no available seats

        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(tripSnapshot);
        _factory.TripClient.LockSeatsAsync(
                tripId,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new LockSeatsOutcome.SeatUnavailable(["B02"]));

        var userId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(userId);
        var idempotencyKey = Guid.NewGuid().ToString();

        var body = JsonSerializer.Serialize(new
        {
            tripId,
            pickup = new { stationId },
            seats = new[]
            {
                new
                {
                    seatNumber = "B02",
                    passenger = new { fullName = "Tran Thi B", phoneNumber = "0900000002", idNumber = "012345678902" },
                },
            },
            paymentMethod = "WALLET",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/bookings")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        // Act
        var response = await client.SendAsync(request);

        // Assert: 409 with BOOKING_SEAT_UNAVAILABLE error code
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("statusCode").GetInt32().Should().Be(409);
        root.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("BOOKING_SEAT_UNAVAILABLE");
        root.GetProperty("meta").GetProperty("traceId").GetString()
            .Should().NotBeNullOrEmpty();

        // All-or-nothing: no charge, no book-seats, and no booking row persisted
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .ChargeAsync(default!, default, default, default, default!, default!, default);
        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .BookSeatsAsync(default, default, default, default!, default);
        await _factory.BookingRepository.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
    }

    // -----------------------------------------------------------------------
    // Test 3 (B2 requirement): book-seats fails after payment → release-seats
    // called (compensation ordering assert via mock)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostBookings_BookSeatsFails_ReleasesSeatsAndReturns409()
    {
        // Arrange: clear any call history from previous tests in this shared fixture
        _factory.TripClient.ClearReceivedCalls();
        _factory.PaymentClient.ClearReceivedCalls();
        _factory.BookingRepository.ClearReceivedCalls();

        var tripId = Guid.NewGuid();
        var lockToken = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        var tripSnapshot = new TripSnapshot(
            TripId: tripId,
            OperatorId: Guid.NewGuid(),
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: "SCHEDULED",
            DepartureDateTime: DateTimeOffset.UtcNow.AddHours(2),
            EstimatedArrivalTime: DateTimeOffset.UtcNow.AddHours(4),
            BaseFare: 180_000,
            OriginStation: new TripStationSnapshot(stationId, "Hà Nội"),
            DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "TP.HCM"),
            Stops: [],
            SeatSummary: new TripSeatSummary(40, 10));

        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(tripSnapshot);
        _factory.TripClient.LockSeatsAsync(
                tripId,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new LockSeatsOutcome.Success(
                new SeatLockResult(lockToken, ["C03"], DateTimeOffset.UtcNow.AddMinutes(10))));
        _factory.PaymentClient.ChargeAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<PaymentContextSnapshot?>())
            .Returns(new ChargeOutcome.Success(
                new ChargeResult(Guid.NewGuid(), "SUCCEEDED", null)));
        // book-seats returns false (lock expired)
        _factory.TripClient.BookSeatsAsync(
                tripId,
                lockToken,
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
                Arg.Any<CancellationToken>())
            .Returns(false);
        // ReleaseSeatsAsync is a no-op (void-returning task)
        _factory.TripClient.ReleaseSeatsAsync(
                tripId,
                lockToken,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var userId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(userId);
        var idempotencyKey = Guid.NewGuid().ToString();

        var body = JsonSerializer.Serialize(new
        {
            tripId,
            pickup = new { stationId },
            seats = new[]
            {
                new
                {
                    seatNumber = "C03",
                    passenger = new { fullName = "Le Van C", phoneNumber = "0900000003", idNumber = "012345678903" },
                },
            },
            paymentMethod = "WALLET",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/bookings")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        // Act
        var response = await client.SendAsync(request);

        // Assert: 409 with BOOKING_SEAT_UNAVAILABLE (lock expired)
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("BOOKING_SEAT_UNAVAILABLE");

        // Compensation ordering assert: release-seats must have been called
        await _factory.TripClient.Received()
            .ReleaseSeatsAsync(
                tripId,
                lockToken,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostBookings_MoreThanFiveSeats_Returns422BookingMaxSeatsExceeded()
    {
        _factory.TripClient.ClearReceivedCalls();
        _factory.PaymentClient.ClearReceivedCalls();
        _factory.BookingRepository.ClearReceivedCalls();

        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var body = JsonSerializer.Serialize(new
        {
            tripId = Guid.NewGuid(),
            pickup = new { stationId = Guid.NewGuid() },
            seats = Enumerable.Range(1, 6).Select(i => new
            {
                seatNumber = $"A{i:D2}",
                passenger = new
                {
                    fullName = $"Passenger {i}",
                    phoneNumber = $"090000000{i}",
                    idNumber = $"01234567890{i}",
                },
            }),
            paymentMethod = "WALLET",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/bookings")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("statusCode").GetInt32().Should().Be(422);
        root.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("BOOKING_MAX_SEATS_EXCEEDED");
        root.GetProperty("meta").GetProperty("traceId").GetString()
            .Should().NotBeNullOrEmpty();

        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .GetTripSnapshotAsync(default, default);
        await _factory.BookingRepository.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
    }

    [Fact]
    public async Task PostBookings_NonPassengerRole_Returns403_BeforeHandler()
    {
        _factory.TripClient.ClearReceivedCalls();
        _factory.PaymentClient.ClearReceivedCalls();
        _factory.BookingRepository.ClearReceivedCalls();

        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid(), role: "OPERATOR_STAFF");
        var body = JsonSerializer.Serialize(new
        {
            tripId = Guid.NewGuid(),
            pickup = new { stationId = Guid.NewGuid() },
            seats = new[]
            {
                new
                {
                    seatNumber = "A01",
                    passenger = new { fullName = "Nguyen Van A", phoneNumber = "0900000001", idNumber = "012345678901" },
                },
            },
            paymentMethod = "WALLET",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/bookings")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .GetTripSnapshotAsync(default, default);
        await _factory.BookingRepository.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
    }
}

/// <summary>
/// WebApplicationFactory for CreateBooking integration tests.
/// Exposes NSubstitute mocks for ITripServiceClient and IPaymentServiceClient
/// so each test can configure mock responses via ConfigureTestServices.
/// </summary>
public class CreateBookingWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    public ITripServiceClient TripClient { get; } = Substitute.For<ITripServiceClient>();
    public IPaymentServiceClient PaymentClient { get; } = Substitute.For<IPaymentServiceClient>();

    /// <summary>
    /// Mock repository — exposed so tests can assert AddAsync was or was not called
    /// (all-or-nothing proof).
    /// </summary>
    public IBookingRepository BookingRepository { get; } = Substitute.For<IBookingRepository>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseEnvironment("Testing");

        // ConfigureTestServices runs AFTER all app registrations (AddVietRideDbContext /
        // AddInfrastructure in Program.cs) and wins last-registration — so mocks shadow
        // the real EF/Redis/HTTP registrations rather than being shadowed by them.
        builder.ConfigureTestServices(services =>
        {
            // Replace ITripServiceClient and IPaymentServiceClient with mocks.
            services.AddSingleton(TripClient);
            services.AddSingleton(PaymentClient);

            // Replace IBookingRepository with a mock — the mock's AddAsync returns the entity
            // so the handler can access booking.Id and booking.Passengers after Add.
            BookingRepository.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<BookingEntity>());
            services.AddSingleton(BookingRepository);

            // Replace IUnitOfWork with a stub that executes the operation directly without
            // opening a DB transaction (no live Postgres required for these tests).
            var mockUow = Substitute.For<IUnitOfWork>();
            mockUow.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<CreateBookingResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var op = ci.Arg<Func<Task<CreateBookingResult>>>();
                    return op();
                });
            services.AddSingleton(mockUow);

            services.AddSingleton<IConnectionMultiplexer>(InMemoryIdempotencyRedis.Create());
        });
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with a valid Internal JWT in
    /// <c>X-Internal-Auth</c> carrying the given <paramref name="userId"/> as sub.
    /// </summary>
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
