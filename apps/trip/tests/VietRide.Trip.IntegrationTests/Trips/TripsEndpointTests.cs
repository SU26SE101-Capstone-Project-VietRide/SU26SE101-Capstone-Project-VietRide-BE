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
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;
using VietRide.Trip.Application.Features.Trips.SearchTrips;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class TripsEndpointTests
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task Search_Happy_ReturnsPagedApiResponseEnvelope()
    {
        var tripId = Guid.NewGuid();
        var originStationId = Guid.NewGuid();
        var destinationStationId = Guid.NewGuid();
        var result = SearchTripsResult.Create([
            new SearchTripItem(
                tripId,
                Guid.NewGuid(),
                "VietRide Express",
                Guid.NewGuid(),
                DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"),
                DateTimeOffset.Parse("2026-05-18T20:00:00+07:00"),
                new SearchTripStationDto(originStationId, "Bến xe Miền Đông"),
                new SearchTripStationDto(destinationStationId, "Bến xe Mỹ Đình"),
                18,
                400000,
                true,
                true)],
            1,
            20,
            1);
        var mediator = new StubMediator(_ => result);
        using var factory = new TripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/v1/trips/search?originStationId={originStationId}&destinationStationId={destinationStationId}&departureDate=2026-05-18&passengerCount=2&allowAlongRoutePickup=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(document, 200);
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("items").GetArrayLength().Should().Be(1);
        data.GetProperty("items")[0].GetProperty("tripId").GetGuid().Should().Be(tripId);
        data.GetProperty("items")[0].GetProperty("availableSeats").GetInt32().Should().Be(18);
        mediator.LastRequest.Should().BeOfType<SearchTripsQuery>()
            .Which.Should().BeEquivalentTo(new SearchTripsQuery(originStationId, destinationStationId, new DateOnly(2026, 5, 18), 2, true));
    }

    [Fact]
    public async Task Search_NoResult_Returns200EmptyItems()
    {
        var mediator = new StubMediator(_ => SearchTripsResult.Create([], 1, 20, 0));
        using var factory = new TripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/v1/trips/search?originStationId={Guid.NewGuid()}&destinationStationId={Guid.NewGuid()}&departureDate=2026-05-18&passengerCount=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(document, 200);
        document.RootElement.GetProperty("data").GetProperty("items").GetArrayLength().Should().Be(0);
        document.RootElement.GetProperty("data").GetProperty("totalItems").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task Search_InvalidRequest_Returns422Envelope()
    {
        var mediator = new StubMediator(_ => throw new ValidationException(
            "Trip search request is invalid.",
            [new ValidationError(nameof(SearchTripsQuery.PassengerCount), "Passenger count must be positive.")]));
        using var factory = new TripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/v1/trips/search?originStationId={Guid.NewGuid()}&destinationStationId={Guid.NewGuid()}&departureDate=2026-05-18&passengerCount=0");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorEnvelopeAsync(response, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetDetail_Happy_ReturnsApiResponseEnvelope()
    {
        var tripId = Guid.NewGuid();
        var detail = CreateDetail(tripId);
        var mediator = new StubMediator(_ => detail);
        using var factory = new TripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"/v1/trips/{tripId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(document, 200);
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("tripId").GetGuid().Should().Be(tripId);
        data.GetProperty("seatSummary").GetProperty("availableSeats").GetInt32().Should().Be(18);
        data.GetProperty("fareBreakdown").GetProperty("baseFare").GetInt64().Should().Be(400000);
        mediator.LastRequest.Should().BeOfType<GetTripDetailQuery>()
            .Which.TripId.Should().Be(tripId);
    }

    [Fact]
    public async Task GetDetail_UnknownTrip_Returns404TripNotFoundEnvelope()
    {
        var mediator = new StubMediator(_ => throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found."));
        using var factory = new TripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"/v1/trips/{Guid.NewGuid()}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorEnvelopeAsync(response, "TRIP_NOT_FOUND");
    }

    [Fact]
    public async Task GetSeatMap_Happy_ReturnsApiResponseEnvelope()
    {
        var tripId = Guid.NewGuid();
        var seatMap = new TripSeatMapDto(
            tripId,
            "SLEEPER_BUS",
            [new TripSeatMapSeatDto("A01", "AVAILABLE", "SLEEPER_LOWER", 1, 1, 1)]);
        var mediator = new StubMediator(_ => seatMap);
        using var factory = new TripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"/v1/trips/{tripId}/seat-map"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(document, 200);
        var seat = document.RootElement.GetProperty("data").GetProperty("seats")[0];
        seat.GetProperty("seatNumber").GetString().Should().Be("A01");
        seat.GetProperty("status").GetString().Should().Be("AVAILABLE");
        seat.GetProperty("type").GetString().Should().Be("SLEEPER_LOWER");
        seat.GetProperty("row").GetInt32().Should().Be(1);
        seat.GetProperty("col").GetInt32().Should().Be(1);
        seat.GetProperty("deck").GetInt32().Should().Be(1);
        mediator.LastRequest.Should().BeOfType<GetTripSeatMapQuery>()
            .Which.TripId.Should().Be(tripId);
    }

    [Fact]
    public async Task GetSeatMap_UnknownTrip_Returns404TripNotFoundEnvelope()
    {
        var mediator = new StubMediator(_ => throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found."));
        using var factory = new TripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"/v1/trips/{Guid.NewGuid()}/seat-map"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorEnvelopeAsync(response, "TRIP_NOT_FOUND");
    }

    private static TripDetailDto CreateDetail(Guid tripId)
    {
        var stopId = Guid.NewGuid();
        return new TripDetailDto(
            tripId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SCHEDULED",
            DateTimeOffset.Parse("2026-05-18T08:00:00+07:00"),
            DateTimeOffset.Parse("2026-05-18T20:00:00+07:00"),
            400000,
            new TripStationDto(Guid.NewGuid(), "Bến xe Miền Đông"),
            new TripStationDto(Guid.NewGuid(), "Bến xe Mỹ Đình"),
            [new TripStopDto(stopId, 1, true, false, DateTimeOffset.Parse("2026-05-18T09:30:00+07:00"), 42.5, 350000)],
            new TripSeatSummaryDto(40, 18),
            null,
            new TripFareBreakdownDto(400000, [new TripFareStopDto(stopId, 350000)]));
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt()}");
        return request;
    }

    private static string CreateInternalJwt()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "PASSENGER")],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void AssertSuccessEnvelope(JsonDocument document, int statusCode)
    {
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be(statusCode);
        document.RootElement.TryGetProperty("data", out _).Should().BeTrue();
        document.RootElement.GetProperty("meta").GetProperty("traceId").GetString().Should().NotBeNull();
    }

    private static async Task AssertErrorEnvelopeAsync(HttpResponseMessage response, string errorCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)response.StatusCode);
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(errorCode);
        document.RootElement.GetProperty("meta").GetProperty("traceId").GetString().Should().NotBeNull();
    }

    private sealed class TripsEndpointWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly IMediator mediator;

        public TripsEndpointWebApplicationFactory(IMediator mediator)
        {
            this.mediator = mediator;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=5432;Database=test;Username=vietride;Password=vietride_dev");
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
