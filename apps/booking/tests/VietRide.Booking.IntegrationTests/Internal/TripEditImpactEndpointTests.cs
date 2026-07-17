using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Internal.Bookings;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;

namespace VietRide.Booking.IntegrationTests.Internal;

public sealed class TripEditImpactEndpointTests
    : IClassFixture<TripEditImpactWebApplicationFactory>
{
    private static readonly Guid TripId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private readonly TripEditImpactWebApplicationFactory factory;

    public TripEditImpactEndpointTests(TripEditImpactWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetTripEditImpact_WithInternalJwt_ReturnsExactRawProjection()
    {
        factory.BookingRepository.ClearReceivedCalls();
        var firstBookingId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var secondBookingId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        factory.BookingRepository.GetTripEditImpactAsync(
                TripId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripEditImpactDto(
                TripId,
                2,
                [
                    new TripEditImpactDto.ActiveBooking(
                        firstBookingId,
                        "PENDING_PAYMENT",
                        ["A01", "A02"]),
                    new TripEditImpactDto.ActiveBooking(
                        secondBookingId,
                        "CONFIRMED",
                        ["B01"]),
                ]));
        using var client = factory.CreateInternalClient();

        var response = await client.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/edit-impact?operatorId={OperatorId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().Equal(
            "tripId",
            "activeBookingCount",
            "activeBookings");
        root.TryGetProperty("success", out _).Should().BeFalse();
        root.GetProperty("tripId").GetGuid().Should().Be(TripId);
        root.GetProperty("activeBookingCount").GetInt32().Should().Be(2);
        var impacts = root.GetProperty("activeBookings");
        impacts.GetArrayLength().Should().Be(2);
        impacts[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "bookingId",
            "status",
            "seatNumbers");
        impacts[0].GetProperty("bookingId").GetGuid().Should().Be(firstBookingId);
        impacts[0].GetProperty("status").GetString().Should().Be("PENDING_PAYMENT");
        impacts[0].GetProperty("seatNumbers").EnumerateArray()
            .Select(value => value.GetString()).Should().Equal("A01", "A02");
        impacts[1].GetProperty("bookingId").GetGuid().Should().Be(secondBookingId);
        impacts[1].GetProperty("status").GetString().Should().Be("CONFIRMED");
        await factory.BookingRepository.Received(1).GetTripEditImpactAsync(
            TripId,
            OperatorId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTripEditImpact_WithNoActiveBookings_ReturnsRawEmptyProjection()
    {
        factory.BookingRepository.ClearReceivedCalls();
        factory.BookingRepository.GetTripEditImpactAsync(
                TripId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripEditImpactDto(TripId, 0, []));
        using var client = factory.CreateInternalClient();

        var response = await client.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/edit-impact?operatorId={OperatorId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.TryGetProperty("success", out _).Should().BeFalse();
        document.RootElement.GetProperty("tripId").GetGuid().Should().Be(TripId);
        document.RootElement.GetProperty("activeBookingCount").GetInt32().Should().Be(0);
        document.RootElement.GetProperty("activeBookings").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?operatorId=")]
    public async Task GetTripEditImpact_WithMissingOrEmptyOperatorId_Returns422(string query)
    {
        factory.BookingRepository.ClearReceivedCalls();
        using var client = factory.CreateInternalClient();

        var response = await client.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/edit-impact{query}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
        await factory.BookingRepository.DidNotReceiveWithAnyArgs()
            .GetTripEditImpactAsync(default, default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetTripEditImpact_WithMissingOrInvalidInternalJwt_Returns401(
        bool addInvalidHeader)
    {
        factory.BookingRepository.ClearReceivedCalls();
        using var client = factory.CreateClient();
        if (addInvalidHeader)
        {
            client.DefaultRequestHeaders.Add("X-Internal-Auth", "Bearer invalid-token");
        }

        var response = await client.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/edit-impact?operatorId={OperatorId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(response, "AUTH_TOKEN_INVALID");
        await factory.BookingRepository.DidNotReceiveWithAnyArgs()
            .GetTripEditImpactAsync(default, default, default);
    }

    private static async Task AssertErrorCodeAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(expectedCode);
    }
}

public sealed class TripEditImpactWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    public IBookingRepository BookingRepository { get; } =
        Substitute.For<IBookingRepository>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(BookingRepository);
        });
    }

    public HttpClient CreateInternalClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(
            "X-Internal-Auth",
            $"Bearer {MintInternalJwt()}");
        return client;
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

[Collection(VietRide.Booking.IntegrationTests.VoucherPersistenceCollection.CollectionName)]
public sealed class TripEditImpactRepositoryTests
    : IClassFixture<VietRide.Booking.IntegrationTests.VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VietRide.Booking.IntegrationTests.VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory;

    public TripEditImpactRepositoryTests(
        VietRide.Booking.IntegrationTests.VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Repository_IncludesOnlyActiveMatchingTenant_AndGroupsSeatsPerBooking()
    {
        await factory.InitializeAsync();
        var tripId = Guid.NewGuid();
        var otherTripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var foreignOperatorId = Guid.NewGuid();
        var includedPendingId = Guid.NewGuid();
        var includedConfirmedId = Guid.NewGuid();
        var foreignBookingId = Guid.NewGuid();

        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var rows = new List<BookingSeed>
            {
                new(includedPendingId, tripId, operatorId, BookingStatus.PENDING_PAYMENT, ["A02", "A01"]),
                new(includedConfirmedId, tripId, operatorId, BookingStatus.CONFIRMED, ["B01"]),
                new(Guid.NewGuid(), tripId, operatorId, BookingStatus.COMPLETED, ["C01"]),
                new(Guid.NewGuid(), tripId, operatorId, BookingStatus.EXPIRED, ["D01"]),
                new(Guid.NewGuid(), tripId, operatorId, BookingStatus.CANCELLED, ["E01"]),
                new(Guid.NewGuid(), tripId, operatorId, BookingStatus.NO_SHOW, ["F01"]),
                new(Guid.NewGuid(), tripId, operatorId, BookingStatus.PARTIAL_NO_SHOW, ["G01"]),
                new(Guid.NewGuid(), tripId, operatorId, BookingStatus.REFUNDED, ["H01"]),
                new(Guid.NewGuid(), tripId, operatorId, BookingStatus.DISRUPTED, ["I01"]),
                new(foreignBookingId, tripId, foreignOperatorId, BookingStatus.CONFIRMED, ["X99"]),
                new(Guid.NewGuid(), otherTripId, operatorId, BookingStatus.CONFIRMED, ["Y99"]),
            };
            await SeedAsync(db, rows);
        }

        await using var readScope = factory.Services.CreateAsyncScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IBookingRepository>();

        var result = await repository.GetTripEditImpactAsync(tripId, operatorId);

        result.TripId.Should().Be(tripId);
        result.ActiveBookingCount.Should().Be(2);
        result.ActiveBookings.Select(impact => impact.BookingId).Should()
            .OnlyHaveUniqueItems()
            .And.BeEquivalentTo([includedPendingId, includedConfirmedId]);
        result.ActiveBookings.Single(impact => impact.BookingId == includedPendingId)
            .Should().BeEquivalentTo(new
            {
                BookingId = includedPendingId,
                Status = "PENDING_PAYMENT",
                SeatNumbers = new[] { "A01", "A02" },
            });
        result.ActiveBookings.Single(impact => impact.BookingId == includedConfirmedId)
            .Should().BeEquivalentTo(new
            {
                BookingId = includedConfirmedId,
                Status = "CONFIRMED",
                SeatNumbers = new[] { "B01" },
            });

        var foreignResult = await repository.GetTripEditImpactAsync(tripId, foreignOperatorId);
        foreignResult.ActiveBookingCount.Should().Be(1);
        foreignResult.ActiveBookings.Should().ContainSingle()
            .Which.BookingId.Should().Be(foreignBookingId);

        var emptyResult = await repository.GetTripEditImpactAsync(tripId, Guid.NewGuid());
        emptyResult.TripId.Should().Be(tripId);
        emptyResult.ActiveBookingCount.Should().Be(0);
        emptyResult.ActiveBookings.Should().BeEmpty();

        Func<Task> emptyOperator = () => repository.GetTripEditImpactAsync(
            tripId,
            Guid.Empty);
        await emptyOperator.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("operatorId");
    }

    private static async Task SeedAsync(
        BookingDbContext db,
        IReadOnlyList<BookingSeed> rows)
    {
        var createdAt = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var bookingCode = $"VR-20260715-AAAAAA{(char)('A' + index)}2";
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.bookings
    (id, booking_code, passenger_user_id, trip_id, operator_id, pickup_station_id,
     base_fare, discount_amount, total_amount, status, refund_override, created_at, updated_at)
VALUES
    ({row.BookingId}, {bookingCode}, {Guid.NewGuid()}, {row.TripId}, {row.OperatorId}, {Guid.NewGuid()},
     100000, 0, 100000, CAST({row.Status.ToString()} AS booking_status), FALSE, {createdAt}, {createdAt});");

            foreach (var seatNumber in row.SeatNumbers)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.passengers
    (id, booking_id, seat_number, boarding_status, created_at, updated_at)
VALUES
    ({Guid.NewGuid()}, {row.BookingId}, {seatNumber}, 'PENDING'::passenger_boarding_status,
     {createdAt}, {createdAt});");
            }
        }
    }

    private sealed record BookingSeed(
        Guid BookingId,
        Guid TripId,
        Guid OperatorId,
        BookingStatus Status,
        IReadOnlyList<string> SeatNumbers);
}
