using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class Day24NoShowOperationalSnapshotIntegrationTests
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private static readonly DateTimeOffset ActualDepartureTime = new(2026, 7, 18, 1, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ActualArrivalTime = ActualDepartureTime.AddHours(1);

    [Fact]
    public async Task GetTrip_ValidInternalJwt_ReturnsBackwardCompatibleRawOperationalSnapshot()
    {
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var snapshot = CreateSnapshot(tripId, stopId);
        var mediator = new StubMediator(_ => snapshot);
        using var factory = new SnapshotWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/trips/{tripId}");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt()}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.TryGetProperty("success", out _).Should().BeFalse("the internal endpoint returns a raw DTO");
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
        [
            "tripId",
            "operatorId",
            "routeId",
            "vehicleId",
            "status",
            "departureDateTime",
            "estimatedArrivalTime",
            "baseFare",
            "originStation",
            "destinationStation",
            "stops",
            "seatSummary",
            "returnRouteId",
            "driverUserId",
            "assistantUserId",
            "destinationArrivedAt",
            "actualDepartureTime",
        ]);
        root.GetProperty("tripId").GetGuid().Should().Be(tripId);
        root.GetProperty("actualDepartureTime").GetDateTimeOffset().Should().Be(ActualDepartureTime);
        root.GetProperty("destinationArrivedAt").GetDateTimeOffset().Should().Be(ActualDepartureTime.AddHours(3));

        var stop = root.GetProperty("stops").EnumerateArray().Should().ContainSingle().Which;
        stop.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
        [
            "stopId",
            "orderIndex",
            "allowPickup",
            "allowDropoff",
            "estimatedArrivalTime",
            "distanceFromOriginKm",
            "fareFromThisStop",
            "status",
            "actualArrivalTime",
            "isActive",
        ]);
        stop.GetProperty("stopId").GetGuid().Should().Be(stopId);
        stop.GetProperty("status").GetString().Should().Be("ARRIVED");
        stop.GetProperty("actualArrivalTime").GetDateTimeOffset().Should().Be(ActualArrivalTime);
        mediator.LastRequest.Should().Be(new GetTripSnapshotQuery(tripId));
    }

    [Fact]
    public async Task GetTrip_InvalidInternalJwt_Returns401AuthTokenInvalidEnvelope()
    {
        var mediator = new StubMediator(_ => throw new InvalidOperationException("Mediator must not be called."));
        using var factory = new SnapshotWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/trips/{Guid.NewGuid()}");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer not-a-valid-jwt");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be(401);
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("AUTH_TOKEN_INVALID");
        mediator.LastRequest.Should().BeNull();
    }

    private static InternalTripSnapshotDto CreateSnapshot(Guid tripId, Guid stopId) => new(
        tripId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "IN_PROGRESS",
        ActualDepartureTime.AddHours(-1),
        ActualDepartureTime.AddHours(4),
        250000,
        new InternalTripStationSnapshotDto(Guid.NewGuid(), "Origin"),
        new InternalTripStationSnapshotDto(Guid.NewGuid(), "Destination"),
        [
            new InternalTripStopSnapshotDto(
                stopId,
                1,
                true,
                true,
                ActualArrivalTime,
                50d,
                200000,
                "ARRIVED",
                ActualArrivalTime),
        ],
        new InternalTripSeatSummaryDto(40, 10),
        null,
        Guid.NewGuid(),
        null,
        ActualDepartureTime.AddHours(3),
        ActualDepartureTime);

    private static string CreateInternalJwt()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class SnapshotWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly IMediator mediator;

        public SnapshotWebApplicationFactory(IMediator mediator)
        {
            this.mediator = mediator;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting(
                "ConnectionStrings:Default",
                global::VietRide.Trip.IntegrationTests.VietRideWebApplicationFactory.ResolveConnectionString("postgres"));
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        }
    }

    private sealed class StubMediator : IMediator
    {
        private readonly Func<object, object?> responder;

        public StubMediator(Func<object, object?> responder)
        {
            this.responder = responder;
        }

        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var response = responder(request);
            return Task.FromResult(response is TResponse typed ? typed : default!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => EmptyStream<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            EmptyStream<object?>();

        private static async IAsyncEnumerable<T> EmptyStream<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
