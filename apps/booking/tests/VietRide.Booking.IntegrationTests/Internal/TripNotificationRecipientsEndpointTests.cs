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

public sealed class TripNotificationRecipientsEndpointTests
    : IClassFixture<TripNotificationRecipientsWebApplicationFactory>
{
    private static readonly Guid TripId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private readonly TripNotificationRecipientsWebApplicationFactory factory;

    public TripNotificationRecipientsEndpointTests(
        TripNotificationRecipientsWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ReturnsExactRawProjectionAndEmptyProjection()
    {
        var bookingId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var userId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        factory.BookingRepository.ClearReceivedCalls();
        factory.BookingRepository.GetTripNotificationRecipientsAsync(
                TripId,
                Arg.Any<CancellationToken>())
            .Returns(
                new TripNotificationRecipientsDto(
                    TripId,
                    [new TripNotificationRecipientDto(bookingId, userId, "CONFIRMED")]),
                new TripNotificationRecipientsDto(TripId, []));
        using var client = factory.CreateInternalClient();

        var response = await client.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/notification-recipients");
        var emptyResponse = await client.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/notification-recipients");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().Equal(
            "tripId",
            "recipients");
        root.TryGetProperty("success", out _).Should().BeFalse();
        root.GetProperty("tripId").GetGuid().Should().Be(TripId);
        var recipients = root.GetProperty("recipients");
        recipients.GetArrayLength().Should().Be(1);
        recipients[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            "bookingId",
            "userId",
            "status");
        recipients[0].GetProperty("bookingId").GetGuid().Should().Be(bookingId);
        recipients[0].GetProperty("userId").GetGuid().Should().Be(userId);
        recipients[0].GetProperty("status").GetString().Should().Be("CONFIRMED");

        emptyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var emptyDocument = JsonDocument.Parse(
            await emptyResponse.Content.ReadAsStringAsync());
        emptyDocument.RootElement.GetProperty("tripId").GetGuid().Should().Be(TripId);
        emptyDocument.RootElement.GetProperty("recipients").GetArrayLength().Should().Be(0);
        await factory.BookingRepository.Received(2).GetTripNotificationRecipientsAsync(
            TripId,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task RejectsMalformedOrEmptyTripId(string tripId)
    {
        factory.BookingRepository.ClearReceivedCalls();
        using var client = factory.CreateInternalClient();

        var response = await client.GetAsync(
            $"/internal/v1/bookings/trips/{tripId}/notification-recipients");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
        await factory.BookingRepository.DidNotReceiveWithAnyArgs()
            .GetTripNotificationRecipientsAsync(default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsMissingOrInvalidInternalJwt(bool addInvalidHeader)
    {
        factory.BookingRepository.ClearReceivedCalls();
        using var client = factory.CreateClient();
        if (addInvalidHeader)
        {
            client.DefaultRequestHeaders.Add("X-Internal-Auth", "Bearer invalid-token");
        }

        var response = await client.GetAsync(
            $"/internal/v1/bookings/trips/{TripId:D}/notification-recipients");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(response, "AUTH_TOKEN_INVALID");
        await factory.BookingRepository.DidNotReceiveWithAnyArgs()
            .GetTripNotificationRecipientsAsync(default, default);
    }

    [Fact]
    public async Task ThinControllerDispatchesMediatRAndDeclaresContractMetadata()
    {
        var expected = new TripNotificationRecipientsDto(TripId, []);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Any<GetTripNotificationRecipientsQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var controller = new InternalBookingsController(mediator);

        var result = await controller.GetTripNotificationRecipientsAsync(
            TripId.ToString("D"),
            CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(expected);
        await mediator.Received(1).Send(
            Arg.Is<GetTripNotificationRecipientsQuery>(query =>
                query.TripId == TripId.ToString("D")),
            Arg.Any<CancellationToken>());

        var method = typeof(InternalBookingsController)
            .GetMethod(nameof(InternalBookingsController.GetTripNotificationRecipientsAsync));
        var metadata = method!
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: false)
            .Cast<ProducesResponseTypeAttribute>()
            .ToArray();
        metadata.Should().ContainSingle(attribute =>
            attribute.StatusCode == StatusCodes.Status200OK
            && attribute.Type == typeof(TripNotificationRecipientsDto));
        metadata.Should().ContainSingle(attribute =>
            attribute.StatusCode == StatusCodes.Status401Unauthorized
            && attribute.Type == typeof(ApiResponse));
        metadata.Should().ContainSingle(attribute =>
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

public sealed class TripNotificationRecipientsWebApplicationFactory
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
                    ["sub"] = "notification-service",
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
public sealed class TripNotificationRecipientsRepositoryTests
    : IClassFixture<VietRide.Booking.IntegrationTests.VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VietRide.Booking.IntegrationTests.VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory;

    public TripNotificationRecipientsRepositoryTests(
        VietRide.Booking.IntegrationTests.VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task RepositoryReturnsOnlyEligibleExactTripRowsInDeterministicOrder()
    {
        await factory.InitializeAsync();
        var tripId = Guid.Parse("51000000-0000-4000-8000-000000000001");
        var otherTripId = Guid.Parse("51000000-0000-4000-8000-000000000002");
        var firstBookingId = Guid.Parse("52000000-0000-4000-8000-000000000001");
        var secondBookingId = Guid.Parse("52000000-0000-4000-8000-000000000002");
        var firstUserId = Guid.Parse("53000000-0000-4000-8000-000000000001");
        var secondUserId = Guid.Parse("53000000-0000-4000-8000-000000000002");
        var createdAt = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await SeedBookingAsync(
            db,
            secondBookingId,
            secondUserId,
            tripId,
            BookingStatus.PARTIAL_NO_SHOW,
            "VR-20260802-RECIP002",
            createdAt);
        await SeedBookingAsync(
            db,
            firstBookingId,
            firstUserId,
            tripId,
            BookingStatus.CONFIRMED,
            "VR-20260802-RECIP001",
            createdAt);
        await SeedBookingAsync(
            db,
            Guid.Parse("52000000-0000-4000-8000-000000000003"),
            Guid.Parse("53000000-0000-4000-8000-000000000003"),
            tripId,
            BookingStatus.PENDING_PAYMENT,
            "VR-20260802-RECIP003",
            createdAt);
        await SeedBookingAsync(
            db,
            Guid.Parse("52000000-0000-4000-8000-000000000004"),
            Guid.Parse("53000000-0000-4000-8000-000000000004"),
            otherTripId,
            BookingStatus.CONFIRMED,
            "VR-20260802-RECIP004",
            createdAt);

        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();

        var result = await repository.GetTripNotificationRecipientsAsync(tripId);
        var empty = await repository.GetTripNotificationRecipientsAsync(Guid.NewGuid());

        result.TripId.Should().Be(tripId);
        result.Recipients.Should().Equal(
            new TripNotificationRecipientDto(firstBookingId, firstUserId, "CONFIRMED"),
            new TripNotificationRecipientDto(secondBookingId, secondUserId, "PARTIAL_NO_SHOW"));
        result.Recipients.Should().OnlyHaveUniqueItems();
        empty.Recipients.Should().BeEmpty();

        Func<Task> emptyTrip = () => repository.GetTripNotificationRecipientsAsync(Guid.Empty);
        await emptyTrip.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("tripId");
    }

    private static Task SeedBookingAsync(
        BookingDbContext db,
        Guid bookingId,
        Guid passengerUserId,
        Guid tripId,
        BookingStatus status,
        string bookingCode,
        DateTimeOffset createdAt)
        => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.bookings
    (id, booking_code, passenger_user_id, trip_id, operator_id, pickup_station_id,
     base_fare, discount_amount, total_amount, status, refund_override, created_at, updated_at)
VALUES
    ({bookingId}, {bookingCode}, {passengerUserId}, {tripId}, {Guid.NewGuid()}, {Guid.NewGuid()},
     100000, 0, 100000, CAST({status.ToString()} AS booking_status), FALSE, {createdAt}, {createdAt});");
}
