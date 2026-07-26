using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Internal.Bookings;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.IntegrationTests.Internal;

public sealed class VehicleSubstitutionImpactEndpointTests
    : IClassFixture<VehicleSubstitutionImpactWebApplicationFactory>
{
    private static readonly Guid TripId =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorId =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private readonly VehicleSubstitutionImpactWebApplicationFactory factory;

    public VehicleSubstitutionImpactEndpointTests(
        VehicleSubstitutionImpactWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ReturnsExactRawOrderedEligibleSnapshot()
    {
        var firstBookingId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var secondBookingId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        var firstPassengerId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
        var secondPassengerId = Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff");
        factory.BookingRepository.ClearReceivedCalls();
        factory.BookingRepository.GetVehicleSubstitutionImpactAsync(
                TripId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new VehicleSubstitutionImpactDto(
                TripId,
                OperatorId,
                [
                    new VehicleSubstitutionImpactDto.BookingImpact(
                        firstBookingId,
                        "CONFIRMED",
                        [
                            new VehicleSubstitutionImpactDto.PassengerImpact(
                                firstPassengerId,
                                "BOARDED",
                                "A01"),
                            new VehicleSubstitutionImpactDto.PassengerImpact(
                                secondPassengerId,
                                "PENDING",
                                null),
                        ]),
                    new VehicleSubstitutionImpactDto.BookingImpact(
                        secondBookingId,
                        "PARTIAL_NO_SHOW",
                        []),
                ]));
        using var client = factory.CreateInternalClient();

        var response = await client.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/vehicle-substitution-impact" +
            $"?operatorId={OperatorId:D}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().Equal(
            "oldTripId",
            "operatorId",
            "bookings");
        root.TryGetProperty("success", out _).Should().BeFalse();
        root.GetProperty("oldTripId").GetGuid().Should().Be(TripId);
        root.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
        var bookings = root.GetProperty("bookings");
        bookings.GetArrayLength().Should().Be(2);
        bookings[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "bookingId",
            "bookingStatus",
            "passengers");
        bookings[0].GetProperty("bookingId").GetGuid().Should().Be(firstBookingId);
        bookings[1].GetProperty("bookingId").GetGuid().Should().Be(secondBookingId);
        var passengers = bookings[0].GetProperty("passengers");
        passengers.GetArrayLength().Should().Be(2);
        passengers[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "passengerId",
            "boardingStatus",
            "originalSeatNumber");
        passengers[0].GetProperty("passengerId").GetGuid().Should().Be(firstPassengerId);
        passengers[1].GetProperty("passengerId").GetGuid().Should().Be(secondPassengerId);
        passengers[1].GetProperty("originalSeatNumber").ValueKind.Should()
            .Be(JsonValueKind.Null);
        root.ToString().Should().NotContain("seatType");
        await factory.BookingRepository.Received(1)
            .GetVehicleSubstitutionImpactAsync(
                TripId,
                OperatorId,
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsInvalidInternalJwtAndInvalidRouteInput()
    {
        factory.BookingRepository.ClearReceivedCalls();
        using var unauthenticatedClient = factory.CreateClient();
        var missingJwt = await unauthenticatedClient.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/vehicle-substitution-impact" +
            $"?operatorId={OperatorId:D}");
        missingJwt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(missingJwt, "AUTH_TOKEN_INVALID");

        using var invalidJwtClient = factory.CreateClient();
        invalidJwtClient.DefaultRequestHeaders.Add(
            "X-Internal-Auth",
            "Bearer invalid-token");
        var invalidJwt = await invalidJwtClient.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/vehicle-substitution-impact" +
            $"?operatorId={OperatorId:D}");
        invalidJwt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(invalidJwt, "AUTH_TOKEN_INVALID");

        using var internalClient = factory.CreateInternalClient();
        var malformedTrip = await internalClient.GetAsync(
            "/internal/v1/bookings/trips/not-a-uuid/vehicle-substitution-impact" +
            $"?operatorId={OperatorId:D}");
        malformedTrip.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(malformedTrip, "VALIDATION_ERROR");

        var malformedOperator = await internalClient.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/vehicle-substitution-impact" +
            "?operatorId=not-a-uuid");
        malformedOperator.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(malformedOperator, "VALIDATION_ERROR");

        await factory.BookingRepository.DidNotReceiveWithAnyArgs()
            .GetVehicleSubstitutionImpactAsync(default, default, default);
    }

    [Fact]
    public async Task ThinControllerDispatchesMediatRAndDeclaresSwashbuckleRawSuccessAndApiResponseErrorMetadata()
    {
        var expected = new VehicleSubstitutionImpactDto(TripId, OperatorId, []);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Any<GetVehicleSubstitutionImpactQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var controller = new InternalBookingsController(mediator);

        var result = await controller.GetVehicleSubstitutionImpactAsync(
            TripId.ToString("D"),
            OperatorId.ToString("D"),
            CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(expected);
        await mediator.Received(1).Send(
            Arg.Is<GetVehicleSubstitutionImpactQuery>(query =>
                query.TripId == TripId.ToString("D")
                && query.OperatorId == OperatorId.ToString("D")),
            Arg.Any<CancellationToken>());

        var method = typeof(InternalBookingsController)
            .GetMethod(nameof(InternalBookingsController.GetVehicleSubstitutionImpactAsync));
        method.Should().NotBeNull();
        var responseMetadata = method!
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: false)
            .Cast<ProducesResponseTypeAttribute>()
            .ToArray();
        responseMetadata.Should().ContainSingle(attribute =>
            attribute.StatusCode == StatusCodes.Status200OK
            && attribute.Type == typeof(VehicleSubstitutionImpactDto));
        responseMetadata.Should().ContainSingle(attribute =>
            attribute.StatusCode == StatusCodes.Status401Unauthorized
            && attribute.Type == typeof(ApiResponse));
        responseMetadata.Should().ContainSingle(attribute =>
            attribute.StatusCode == StatusCodes.Status422UnprocessableEntity
            && attribute.Type == typeof(ApiResponse));
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

public sealed class VehicleSubstitutionImpactWebApplicationFactory
    : WebApplicationFactory<Program>
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
            services.RemoveAll<IUnitOfWork>();
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
public sealed class VehicleSubstitutionImpactRepositoryTests
    : IClassFixture<VietRide.Booking.IntegrationTests.VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VietRide.Booking.IntegrationTests.VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory;

    public VehicleSubstitutionImpactRepositoryTests(
        VietRide.Booking.IntegrationTests.VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task RepositoryFiltersTripOperatorBookingAndPassengerEligibilityAndPreservesNullSeat()
    {
        await factory.InitializeAsync();
        var tripId = Guid.Parse("10000000-0000-4000-8000-000000000001");
        var otherTripId = Guid.Parse("10000000-0000-4000-8000-000000000002");
        var operatorId = Guid.Parse("20000000-0000-4000-8000-000000000001");
        var foreignOperatorId = Guid.Parse("20000000-0000-4000-8000-000000000002");
        var absentOperatorId = Guid.Parse("20000000-0000-4000-8000-000000000003");
        var firstBookingId = Guid.Parse("30000000-0000-4000-8000-000000000001");
        var secondBookingId = Guid.Parse("30000000-0000-4000-8000-000000000002");
        var nullSeatPassengerId = Guid.Parse("40000000-0000-4000-8000-000000000001");
        var boardedPassengerId = Guid.Parse("40000000-0000-4000-8000-000000000002");
        var noShowPassengerId = Guid.Parse("40000000-0000-4000-8000-000000000003");
        var partialPassengerId = Guid.Parse("40000000-0000-4000-8000-000000000004");
        var createdAt = new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE vietride_booking.passengers ALTER COLUMN seat_number DROP NOT NULL;");

        await SeedBookingAsync(
            db,
            firstBookingId,
            tripId,
            operatorId,
            BookingStatus.CONFIRMED,
            "VR-20260725-IMPACT01",
            createdAt);
        await SeedBookingAsync(
            db,
            secondBookingId,
            tripId,
            operatorId,
            BookingStatus.PARTIAL_NO_SHOW,
            "VR-20260725-IMPACT02",
            createdAt);
        await SeedBookingAsync(
            db,
            Guid.Parse("30000000-0000-4000-8000-000000000003"),
            tripId,
            operatorId,
            BookingStatus.COMPLETED,
            "VR-20260725-IMPACT03",
            createdAt);
        await SeedBookingAsync(
            db,
            Guid.Parse("30000000-0000-4000-8000-000000000004"),
            tripId,
            operatorId,
            BookingStatus.NO_SHOW,
            "VR-20260725-IMPACT04",
            createdAt);
        await SeedBookingAsync(
            db,
            Guid.Parse("30000000-0000-4000-8000-000000000005"),
            tripId,
            foreignOperatorId,
            BookingStatus.CONFIRMED,
            "VR-20260725-IMPACT05",
            createdAt);
        await SeedBookingAsync(
            db,
            Guid.Parse("30000000-0000-4000-8000-000000000006"),
            otherTripId,
            operatorId,
            BookingStatus.CONFIRMED,
            "VR-20260725-IMPACT06",
            createdAt);

        await SeedPassengerAsync(
            db,
            boardedPassengerId,
            firstBookingId,
            "A01",
            "BOARDED",
            createdAt);
        await SeedPassengerAsync(
            db,
            nullSeatPassengerId,
            firstBookingId,
            null,
            "PENDING",
            createdAt);
        await SeedPassengerAsync(
            db,
            noShowPassengerId,
            firstBookingId,
            "A03",
            "NO_SHOW",
            createdAt);
        await SeedPassengerAsync(
            db,
            partialPassengerId,
            secondBookingId,
            "B01",
            "PENDING",
            createdAt);
        await SeedPassengerAsync(
            db,
            Guid.Parse("40000000-0000-4000-8000-000000000005"),
            Guid.Parse("30000000-0000-4000-8000-000000000003"),
            "C01",
            "BOARDED",
            createdAt);
        await SeedPassengerAsync(
            db,
            Guid.Parse("40000000-0000-4000-8000-000000000006"),
            Guid.Parse("30000000-0000-4000-8000-000000000004"),
            "D01",
            "NO_SHOW",
            createdAt);
        await SeedPassengerAsync(
            db,
            Guid.Parse("40000000-0000-4000-8000-000000000007"),
            Guid.Parse("30000000-0000-4000-8000-000000000005"),
            "X01",
            "BOARDED",
            createdAt);
        await SeedPassengerAsync(
            db,
            Guid.Parse("40000000-0000-4000-8000-000000000008"),
            Guid.Parse("30000000-0000-4000-8000-000000000006"),
            "Y01",
            "PENDING",
            createdAt);

        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();

        var result = await repository.GetVehicleSubstitutionImpactAsync(tripId, operatorId);
        var foreignResult = await repository.GetVehicleSubstitutionImpactAsync(
            tripId,
            absentOperatorId);

        result.OldTripId.Should().Be(tripId);
        result.OperatorId.Should().Be(operatorId);
        result.Bookings.Select(booking => booking.BookingId).Should().Equal(
            firstBookingId,
            secondBookingId);
        result.Bookings[0].BookingStatus.Should().Be("CONFIRMED");
        result.Bookings[0].Passengers.Select(passenger => passenger.PassengerId).Should().Equal(
            nullSeatPassengerId,
            boardedPassengerId);
        result.Bookings[0].Passengers[0].Should().BeEquivalentTo(new
        {
            PassengerId = nullSeatPassengerId,
            BoardingStatus = "PENDING",
            OriginalSeatNumber = (string?)null,
        });
        result.Bookings[0].Passengers[1].Should().BeEquivalentTo(new
        {
            PassengerId = boardedPassengerId,
            BoardingStatus = "BOARDED",
            OriginalSeatNumber = "A01",
        });
        result.Bookings[0].Passengers.Should().NotContain(passenger =>
            passenger.PassengerId == noShowPassengerId);
        result.Bookings[1].Should().BeEquivalentTo(new
        {
            BookingId = secondBookingId,
            BookingStatus = "PARTIAL_NO_SHOW",
            Passengers = new[]
            {
                new
                {
                    PassengerId = partialPassengerId,
                    BoardingStatus = "PENDING",
                    OriginalSeatNumber = "B01",
                },
            },
        });
        foreignResult.OldTripId.Should().Be(tripId);
        foreignResult.OperatorId.Should().Be(absentOperatorId);
        foreignResult.Bookings.Should().BeEmpty();
    }

    private static Task SeedBookingAsync(
        BookingDbContext db,
        Guid bookingId,
        Guid tripId,
        Guid operatorId,
        BookingStatus status,
        string bookingCode,
        DateTimeOffset createdAt)
        => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.bookings
    (id, booking_code, passenger_user_id, trip_id, operator_id, pickup_station_id,
     base_fare, discount_amount, total_amount, status, refund_override, created_at, updated_at)
VALUES
    ({bookingId}, {bookingCode}, {Guid.NewGuid()}, {tripId}, {operatorId}, {Guid.NewGuid()},
     100000, 0, 100000, CAST({status.ToString()} AS booking_status), FALSE, {createdAt}, {createdAt});");

    private static Task SeedPassengerAsync(
        BookingDbContext db,
        Guid passengerId,
        Guid bookingId,
        string? seatNumber,
        string boardingStatus,
        DateTimeOffset createdAt)
        => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.passengers
    (id, booking_id, seat_number, boarding_status, created_at, updated_at)
VALUES
    ({passengerId}, {bookingId}, {seatNumber},
     CAST({boardingStatus} AS passenger_boarding_status), {createdAt}, {createdAt});");
}
