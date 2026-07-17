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
using VietRide.Booking.Application.Features.Bookings.CreateRoundTripBooking;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

public class CreateRoundTripBookingIntegrationTests
    : IClassFixture<CreateRoundTripBookingIntegrationTests.CreateRoundTripBookingWebApplicationFactory>
{
    private readonly CreateRoundTripBookingWebApplicationFactory _factory;

    public CreateRoundTripBookingIntegrationTests(CreateRoundTripBookingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostRoundTrip_WalletHappyPath_Returns201AndCallsBatchOnce()
    {
        _factory.TripClient.ClearReceivedCalls();
        _factory.PaymentClient.ClearReceivedCalls();
        _factory.BookingRepository.ClearReceivedCalls();

        var outboundTripId = Guid.NewGuid();
        var returnTripId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var returnRouteId = Guid.NewGuid();

        var outboundTrip = new TripSnapshot(
            outboundTripId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SCHEDULED",
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow.AddHours(4),
            200_000,
            new TripStationSnapshot(stationId, "Hà Nội"),
            new TripStationSnapshot(Guid.NewGuid(), "Đà Nẵng"),
            [],
            new TripSeatSummary(40, 38),
            returnRouteId);

        var returnTrip = new TripSnapshot(
            returnTripId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SCHEDULED",
            DateTimeOffset.UtcNow.AddHours(6),
            DateTimeOffset.UtcNow.AddHours(8),
            180_000,
            new TripStationSnapshot(stationId, "Đà Nẵng"),
            new TripStationSnapshot(Guid.NewGuid(), "Hà Nội"),
            [],
            new TripSeatSummary(40, 39),
            null);

        _factory.TripClient.GetTripSnapshotAsync(outboundTripId, Arg.Any<CancellationToken>())
            .Returns(outboundTrip);
        _factory.TripClient.GetTripSnapshotAsync(returnTripId, Arg.Any<CancellationToken>())
            .Returns(returnTrip);
        _factory.TripClient.LockRoundTripSeatsAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>())
            .Returns(new LockRoundTripSeatsOutcome.Success(
                new RoundTripSeatLockResult(outboundTripId, Guid.NewGuid(), ["A01"], DateTimeOffset.UtcNow.AddMinutes(10)),
                new RoundTripSeatLockResult(returnTripId, Guid.NewGuid(), ["A01"], DateTimeOffset.UtcNow.AddMinutes(10))));
        _factory.PaymentClient.BatchChargeAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<BatchChargeItem>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => CreateSuccessfulBatchCharge(ci.Arg<IReadOnlyList<BatchChargeItem>>()));
        _factory.TripClient.BookSeatsAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<PassengerSeatAssignment>>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        var userId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(userId);
        var idempotencyKey = Guid.NewGuid().ToString();

        var body = JsonSerializer.Serialize(new
        {
            outbound = new
            {
                tripId = outboundTripId,
                pickup = new { stationId },
                seats = new[]
                {
                    new
                    {
                        seatNumber = "A01",
                        passenger = new { fullName = "Nguyen Van A", phoneNumber = "0900000001", idNumber = "012345678901" },
                    },
                },
            },
            @return = new
            {
                tripId = returnTripId,
                pickup = new { stationId },
                seats = new[]
                {
                    new
                    {
                        seatNumber = "A01",
                        passenger = new { fullName = "Nguyen Van A", phoneNumber = "0900000001", idNumber = "012345678901" },
                    },
                },
            },
            voucherCode = "SUMMER26",
            paymentMethod = "WALLET",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/bookings/round-trip")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(201);
        root.GetProperty("data").GetProperty("grandTotal").GetInt64().Should().Be(380000);
        root.GetProperty("data").GetProperty("paymentRedirectUrl").ValueKind.Should().Be(JsonValueKind.Null);

        await _factory.PaymentClient.Received(1)
            .BatchChargeAsync(
                userId,
                "WALLET",
                Arg.Any<IReadOnlyList<BatchChargeItem>>(),
                $"charge-round-trip-{idempotencyKey}",
                Arg.Any<CancellationToken>());
        await _factory.TripClient.Received(1)
            .LockRoundTripSeatsAsync(
                outboundTripId,
                Arg.Any<IReadOnlyList<string>>(),
                returnTripId,
                Arg.Any<IReadOnlyList<string>>(),
                userId,
                $"lock-round-trip-{idempotencyKey}",
                600,
                Arg.Any<CancellationToken>());
        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .LockSeatsAsync(default, default!, default, default!, default, default);
    }

    private static BatchChargeOutcome.Success CreateSuccessfulBatchCharge(IReadOnlyList<BatchChargeItem> items)
        => new(
            [
                new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", items[0].ReferenceId, "SUCCEEDED", null),
                new BatchChargePaymentResult(Guid.NewGuid(), "BOOKING", items[1].ReferenceId, "SUCCEEDED", null),
            ]);

    [Fact]
    public async Task PostRoundTrip_WithoutIdempotencyKey_Returns422AndDoesNotCallTripClient()
    {
        _factory.TripClient.ClearReceivedCalls();
        _factory.PaymentClient.ClearReceivedCalls();
        _factory.BookingRepository.ClearReceivedCalls();

        var outboundTripId = Guid.NewGuid();
        var returnTripId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        _factory.TripClient.GetTripSnapshotAsync(outboundTripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshot(
                outboundTripId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "SCHEDULED",
                DateTimeOffset.UtcNow.AddHours(2),
                DateTimeOffset.UtcNow.AddHours(4),
                200_000,
                new TripStationSnapshot(stationId, "Hà Nội"),
                new TripStationSnapshot(Guid.NewGuid(), "Đà Nẵng"),
                [],
                new TripSeatSummary(40, 38),
                null));
        _factory.TripClient.GetTripSnapshotAsync(returnTripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshot(
                returnTripId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "SCHEDULED",
                DateTimeOffset.UtcNow.AddHours(6),
                DateTimeOffset.UtcNow.AddHours(8),
                180_000,
                new TripStationSnapshot(stationId, "Đà Nẵng"),
                new TripStationSnapshot(Guid.NewGuid(), "Hà Nội"),
                [],
                new TripSeatSummary(40, 39),
                null));

        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var body = JsonSerializer.Serialize(new
        {
            outbound = new
            {
                tripId = outboundTripId,
                pickup = new { stationId },
                seats = new[]
                {
                    new
                    {
                        seatNumber = "A01",
                        passenger = new { fullName = "Nguyen Van A", phoneNumber = "0900000001", idNumber = "012345678901" },
                    },
                },
            },
            @return = new
            {
                tripId = returnTripId,
                pickup = new { stationId },
                seats = new[]
                {
                    new
                    {
                        seatNumber = "A01",
                        passenger = new { fullName = "Nguyen Van A", phoneNumber = "0900000001", idNumber = "012345678901" },
                    },
                },
            },
            paymentMethod = "WALLET",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/bookings/round-trip")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");

        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .GetTripSnapshotAsync(default, default!);
        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default);
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .BatchChargeAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task PostRoundTrip_ReturnRouteMissing_Returns422AndDoesNotLockSeats()
    {
        _factory.TripClient.ClearReceivedCalls();
        _factory.PaymentClient.ClearReceivedCalls();
        _factory.BookingRepository.ClearReceivedCalls();

        var outboundTripId = Guid.NewGuid();
        var returnTripId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        _factory.TripClient.GetTripSnapshotAsync(outboundTripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshot(
                outboundTripId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "SCHEDULED",
                DateTimeOffset.UtcNow.AddHours(2),
                DateTimeOffset.UtcNow.AddHours(4),
                200_000,
                new TripStationSnapshot(stationId, "Hà Nội"),
                new TripStationSnapshot(Guid.NewGuid(), "Đà Nẵng"),
                [],
                new TripSeatSummary(40, 38),
                null));
        _factory.TripClient.GetTripSnapshotAsync(returnTripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshot(
                returnTripId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "SCHEDULED",
                DateTimeOffset.UtcNow.AddHours(6),
                DateTimeOffset.UtcNow.AddHours(8),
                180_000,
                new TripStationSnapshot(stationId, "Đà Nẵng"),
                new TripStationSnapshot(Guid.NewGuid(), "Hà Nội"),
                [],
                new TripSeatSummary(40, 39),
                null));

        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var body = JsonSerializer.Serialize(new
        {
            outbound = new
            {
                tripId = outboundTripId,
                pickup = new { stationId },
                seats = new[]
                {
                    new
                    {
                        seatNumber = "A01",
                        passenger = new { fullName = "Nguyen Van A", phoneNumber = "0900000001", idNumber = "012345678901" },
                    },
                },
            },
            @return = new
            {
                tripId = returnTripId,
                pickup = new { stationId },
                seats = new[]
                {
                    new
                    {
                        seatNumber = "A01",
                        passenger = new { fullName = "Nguyen Van A", phoneNumber = "0900000001", idNumber = "012345678901" },
                    },
                },
            },
            paymentMethod = "WALLET",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/bookings/round-trip")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("ROUTE_RETURN_NOT_CONFIGURED");

        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .LockRoundTripSeatsAsync(default, default!, default, default!, default, default!, default, default);
        await _factory.PaymentClient.DidNotReceiveWithAnyArgs()
            .BatchChargeAsync(default, default!, default!, default!, default);
    }
    public sealed class CreateRoundTripBookingWebApplicationFactory : WebApplicationFactory<Program>
    {
        private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

        public ITripServiceClient TripClient { get; } = Substitute.For<ITripServiceClient>();
        public IPaymentServiceClient PaymentClient { get; } = Substitute.For<IPaymentServiceClient>();
        public IBookingRepository BookingRepository { get; } = Substitute.For<IBookingRepository>();
        public IVoucherService VoucherService { get; } = Substitute.For<IVoucherService>();
        public IVoucherRepository VoucherRepository { get; } = Substitute.For<IVoucherRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting(
                "ConnectionStrings:Default",
                "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
            builder.UseEnvironment("Testing");

            // Default stub: ValidateAndComputeDiscountAsync returns zero discount (no voucher applied).
            // Individual tests can override via _factory.VoucherService.
            VoucherService.ValidateAndComputeDiscountAsync(
                    Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                    Arg.Any<Money>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(ci => new VoucherValidationResult(Guid.NewGuid(), Money.Zero));
            VoucherService.RecordUsageAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(),
                    Arg.Any<Money>(), Arg.Any<CancellationToken>())
                .Returns(Guid.NewGuid());

            // Default stub: VoucherRepository.GetByIdAsync returns an unlimited VIETRIDE_FUNDED
            // voucher so ComputeAllowedLegsAsync returns allowed=2 (both legs allowed).
            // ValidateAndComputeDiscountAsync above already returns discount=Money.Zero, so
            // grandTotal is unchanged regardless of limits here.
            // Tests that send voucherCode may override this stub to exercise specific cap paths;
            // tests that omit voucherCode (voucherCode=null) never reach ComputeAllowedLegsAsync.
            var now = DateTimeOffset.UtcNow;
            var unlimitedVoucher = Voucher.Create(
                code: "SUMMER26",
                name: "Integration Test Voucher",
                type: VoucherType.FIXED_AMOUNT,
                value: 1_000,
                minOrderAmount: Money.FromRaw(0),
                maxDiscountAmount: null,
                totalUsageLimit: null,
                perUserLimit: null,
                validFrom: now.AddDays(-1),
                validUntil: now.AddDays(30),
                applicableOperatorIds: null,
                applicableRouteIds: null,
                fundingType: VoucherFundingType.VIETRIDE_FUNDED,
                ownerOperatorId: null,
                createdByUserId: Guid.NewGuid());
            VoucherRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(unlimitedVoucher);

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(TripClient);
                services.AddSingleton(PaymentClient);
                services.AddSingleton<IBookingStationCanonicalizer>(
                    PassthroughBookingStationCanonicalizer.Instance);
                services.AddSingleton(VoucherService);
                services.AddSingleton(VoucherRepository);

                BookingRepository.AddAsync(Arg.Any<BookingEntity>(), Arg.Any<CancellationToken>())
                    .Returns(ci => ci.Arg<BookingEntity>());
                services.AddSingleton(BookingRepository);

                var mockUow = Substitute.For<IUnitOfWork>();
                mockUow.ExecuteInTransactionAsync(
                        Arg.Any<Func<Task<CreateRoundTripBookingResult>>>(),
                        Arg.Any<CancellationToken>())
                    .Returns(ci =>
                    {
                        var op = ci.Arg<Func<Task<CreateRoundTripBookingResult>>>();
                        return op();
                    });
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
}
