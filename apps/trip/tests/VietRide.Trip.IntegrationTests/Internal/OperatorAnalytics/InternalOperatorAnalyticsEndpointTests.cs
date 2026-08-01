using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using VietRide.Trip.Application.Features.Internal.OperatorAnalytics;
using VietRide.Trip.IntegrationTests.Internal.Trips;

namespace VietRide.Trip.IntegrationTests.Internal.OperatorAnalytics;

public sealed class InternalOperatorAnalyticsEndpointTests
{
    [Fact]
    public async Task VehicleCounts_InternalJwtReturnsRawArrayWithoutIdempotencyKey()
    {
        var operatorId = Guid.NewGuid();
        var mediator = new StubMediator(_ => new[] { new OperatorVehicleCountResponse(operatorId, 3) });
        using var factory = new EndpointFactory(mediator);
        using var client = factory.CreateClient();
        using var request = Authorized(HttpMethod.Post, "/internal/v1/operators/vehicle-counts/batch");
        request.Content = JsonContent.Create(new { operatorIds = new[] { operatorId } });

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement[0].GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        document.RootElement[0].GetProperty("vehicleCount").GetInt32().Should().Be(3);
        mediator.LastRequest.Should().BeOfType<GetOperatorVehicleCountsQuery>()
            .Which.OperatorIds.Should().Equal(operatorId);
    }

    [Fact]
    public async Task RoutePerformance_InternalJwtReturnsRawArrayAndTrustedPathTenant()
    {
        var operatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var mediator = new StubMediator(_ => new[]
        {
            new OperatorRoutePerformanceResponse(routeId, "A route", "Origin", "Destination", 4, 3),
        });
        using var factory = new EndpointFactory(mediator);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/internal/v1/operators/{operatorId}/route-performance?month=2026-07"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement[0].GetProperty("routeId").GetGuid().Should().Be(routeId);
        document.RootElement[0].GetProperty("completedTripCount").GetInt32().Should().Be(3);
        mediator.LastRequest.Should().Be(new GetOperatorRoutePerformanceQuery(operatorId, "2026-07"));
    }

    [Theory]
    [InlineData("POST", "/internal/v1/operators/vehicle-counts/batch")]
    [InlineData("GET", "/internal/v1/operators/11111111-1111-4111-8111-111111111111/route-performance?month=2026-07")]
    public async Task AnonymousAndUserBearerAreRejected(string method, string path)
    {
        using var factory = new InternalTripsWebApplicationFactory();
        using var client = factory.CreateClient();

        using var anonymous = CreateRequest(new HttpMethod(method), path);
        using var anonymousResponse = await client.SendAsync(anonymous);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var userBearer = CreateRequest(new HttpMethod(method), path);
        userBearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateUserJwt());
        using var userBearerResponse = await client.SendAsync(userBearer);
        userBearerResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidVehicleBatchReturns422WithoutRequiringIdempotencyKey()
    {
        using var factory = new InternalTripsWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = Authorized(HttpMethod.Post, "/internal/v1/operators/vehicle-counts/batch");
        request.Content = JsonContent.Create(new { operatorIds = Array.Empty<Guid>() });

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task SwaggerDocumentsRawSchemasAndNoIdempotencyHeader()
    {
        var mediator = new StubMediator(_ => Array.Empty<OperatorVehicleCountResponse>());
        using var factory = new EndpointFactory(mediator);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var batch = paths.GetProperty("/internal/v1/operators/vehicle-counts/batch").GetProperty("post");
        batch.TryGetProperty("parameters", out var parameters).Should().BeFalse(
            parameters.ValueKind == JsonValueKind.Undefined ? string.Empty : parameters.ToString());
        batch.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("type").GetString()
            .Should().Be("array");
        paths.GetProperty("/internal/v1/operators/{operatorId}/route-performance")
            .GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("type").GetString()
            .Should().Be("array");
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = CreateRequest(method, path);
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt()}");
        return request;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new { operatorIds = new[] { Guid.NewGuid() } });
        }

        return request;
    }

    private static string CreateInternalJwt()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InternalTripsWebApplicationFactory.TestSecret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateUserJwt()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InternalTripsWebApplicationFactory.TestSecret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-identity",
            audience: "vietride-api",
            claims: [new Claim("sub", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "SYSTEM_ADMIN")],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class EndpointFactory : WebApplicationFactory<Program>
    {
        private readonly IMediator mediator;

        public EndpointFactory(IMediator mediator)
        {
            this.mediator = mediator;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", InternalTripsWebApplicationFactory.TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting(
                "ConnectionStrings:Default",
                VietRideWebApplicationFactory.ResolveConnectionString("postgres"));
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

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
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

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => Empty<object?>();

        private static async IAsyncEnumerable<T> Empty<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
