using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Features.AlternativeRoutes;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.AlternativeRoutes;

public sealed class AlternativeRouteDetailEndpointTests
{
    private const string TestSecret = "test-secret-at-least-32-characters-long";
    private const string PathPolyline = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";

    [Fact]
    public async Task FullFlow_ReadsPersistedGeometryWithoutExpandingListAndKeepsInactiveRouteReadable()
    {
        var databaseName = $"vietride_trip_alt_route_detail_{Guid.NewGuid():N}";
        using var factory = new AlternativeRouteWebApplicationFactory(databaseName);

        try
        {
            var seed = await SeedRouteGraphAsync(factory.Services);
            using var client = factory.CreateClient();

            using var createResponse = await SendAuthorizedAsync(
                client,
                HttpMethod.Post,
                $"/v1/operator/routes/{seed.RouteId}/alternative-routes",
                "OPERATOR_ADMIN",
                seed.OperatorId,
                new
                {
                    name = "Persisted geometry bypass",
                    description = "Integration route",
                    destinationStationId = seed.AlternativeDestinationStationId,
                    totalDistanceKm = 321.5m,
                    estimatedDurationMinutes = 455,
                    stops = new[]
                    {
                        new
                        {
                            stopId = seed.StopId,
                            orderIndex = 1,
                            estimatedDurationFromOriginMinutes = 80,
                            distanceFromOriginKm = 70.25m,
                        },
                    },
                });
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            using var createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var alternativeRouteId = createDocument.RootElement.GetProperty("data").GetProperty("id").GetGuid();

            using var geometryResponse = await SendAuthorizedAsync(
                client,
                HttpMethod.Put,
                $"/v1/operator/alternative-routes/{alternativeRouteId}/geometry",
                "OPERATOR_ADMIN",
                seed.OperatorId,
                new { pathPolyline = PathPolyline });
            geometryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var listResponse = await SendAuthorizedAsync(
                client,
                HttpMethod.Get,
                $"/v1/operator/routes/{seed.RouteId}/alternative-routes?page=1&pageSize=20",
                "OPERATOR_STAFF",
                seed.OperatorId);
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            var listItem = listDocument.RootElement.GetProperty("data").GetProperty("items")[0];
            listItem.GetProperty("id").GetGuid().Should().Be(alternativeRouteId);
            listItem.TryGetProperty("pathPolyline", out _).Should().BeFalse();

            using var detailResponse = await SendAuthorizedAsync(
                client,
                HttpMethod.Get,
                $"/v1/operator/alternative-routes/{alternativeRouteId}",
                "OPERATOR_ADMIN",
                seed.OperatorId);
            detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using var detailDocument = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
            AssertSuccessEnvelope(detailDocument, HttpStatusCode.OK);
            var detail = detailDocument.RootElement.GetProperty("data");
            detail.GetProperty("pathPolyline").GetString().Should().Be(PathPolyline);
            detail.GetProperty("isActive").GetBoolean().Should().BeTrue();
            detail.GetProperty("stops")[0].GetProperty("stopId").GetGuid().Should().Be(seed.StopId);

            using var deleteResponse = await SendAuthorizedAsync(
                client,
                HttpMethod.Delete,
                $"/v1/operator/alternative-routes/{alternativeRouteId}",
                "OPERATOR_ADMIN",
                seed.OperatorId);
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var inactiveDetailResponse = await SendAuthorizedAsync(
                client,
                HttpMethod.Get,
                $"/v1/operator/alternative-routes/{alternativeRouteId}",
                "OPERATOR_STAFF",
                seed.OperatorId);
            inactiveDetailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using var inactiveDocument = JsonDocument.Parse(await inactiveDetailResponse.Content.ReadAsStringAsync());
            var inactiveDetail = inactiveDocument.RootElement.GetProperty("data");
            inactiveDetail.GetProperty("isActive").GetBoolean().Should().BeFalse();
            inactiveDetail.GetProperty("pathPolyline").GetString().Should().Be(PathPolyline);

            using var crossTenantResponse = await SendAuthorizedAsync(
                client,
                HttpMethod.Get,
                $"/v1/operator/alternative-routes/{alternativeRouteId}",
                "OPERATOR_ADMIN",
                Guid.NewGuid());
            crossTenantResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            await AssertErrorEnvelopeAsync(crossTenantResponse, "ROUTE_NOT_FOUND");
        }
        finally
        {
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            await cleanupScope.ServiceProvider.GetRequiredService<TripDbContext>().Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task AuthorizationAndMalformedId_RejectBeforeQueryDispatch()
    {
        var responseDto = new AlternativeRouteDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Alt",
            null,
            Guid.NewGuid(),
            null,
            null,
            PathPolyline,
            true,
            [],
            default,
            default);
        var mediator = new RecordingMediator(responseDto);
        using var factory = new StubAlternativeRouteWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        using var wrongRoleResponse = await SendAuthorizedAsync(
            client,
            HttpMethod.Get,
            $"/v1/operator/alternative-routes/{responseDto.Id}",
            "PASSENGER",
            Guid.NewGuid());
        wrongRoleResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var missingScopeResponse = await SendAuthorizedAsync(
            client,
            HttpMethod.Get,
            $"/v1/operator/alternative-routes/{responseDto.Id}",
            "OPERATOR_ADMIN",
            null);
        missingScopeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertErrorEnvelopeAsync(missingScopeResponse, "FORBIDDEN");

        using var malformedIdResponse = await SendAuthorizedAsync(
            client,
            HttpMethod.Get,
            "/v1/operator/alternative-routes/not-a-uuid",
            "OPERATOR_ADMIN",
            Guid.NewGuid());
        malformedIdResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        mediator.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Swagger_DeclaresAlternativeRouteDetailResponseAndErrors()
    {
        var mediator = new RecordingMediator(Unit.Value);
        using var factory = new StubAlternativeRouteWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/v1/operator/alternative-routes/{id}")
            .GetProperty("get");
        var responses = operation.GetProperty("responses");
        responses.EnumerateObject().Select(item => item.Name)
            .Should().Contain(["200", "403", "404"]);
        operation.ToString().Should().Contain("AlternativeRouteDto");
    }

    private static async Task<Seed> SeedRouteGraphAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await dbContext.Database.MigrateAsync();

        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Polyline origin",
            $"polyline-origin-{Guid.NewGuid():N}",
            "Origin city",
            "Origin ward",
            latitude: 38.5m,
            longitude: -120.2m);
        var mainDestination = Station.Create(
            "Main destination",
            $"main-destination-{Guid.NewGuid():N}",
            "Main city",
            "Main ward");
        var alternativeDestination = Station.Create(
            "Alternative destination",
            $"alternative-destination-{Guid.NewGuid():N}",
            "Alternative city",
            "Alternative ward",
            latitude: 43.252m,
            longitude: -126.453m);
        var stop = Stop.Create(operatorId, "Polyline stop", 40.7m, -120.95m);
        var route = Route.Create(
            operatorId,
            "Main route",
            origin.Id,
            mainDestination.Id,
            Money.FromRaw(250_000),
            300m,
            420);

        dbContext.Stations.AddRange(origin, mainDestination, alternativeDestination);
        dbContext.Stops.Add(stop);
        dbContext.Routes.Add(route);
        await dbContext.SaveChangesAsync();

        return new Seed(operatorId, route.Id, alternativeDestination.Id, stop.Id);
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string role,
        Guid? operatorId,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateInternalJwt(role, operatorId)}");
        if (method != HttpMethod.Get)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await client.SendAsync(request);
    }

    private static string CreateInternalJwt(string role, Guid? operatorId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role),
        };
        if (operatorId.HasValue)
            claims.Add(new Claim("operatorId", operatorId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void AssertSuccessEnvelope(JsonDocument document, HttpStatusCode statusCode)
    {
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)statusCode);
        document.RootElement.GetProperty("meta").GetProperty("traceId").GetString().Should().NotBeNull();
    }

    private static async Task AssertErrorEnvelopeAsync(HttpResponseMessage response, string errorCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)response.StatusCode);
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(errorCode);
    }

    private sealed record Seed(
        Guid OperatorId,
        Guid RouteId,
        Guid AlternativeDestinationStationId,
        Guid StopId);

    private sealed class AlternativeRouteWebApplicationFactory(string databaseName) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting(
                "ConnectionStrings:Default",
                VietRideWebApplicationFactory.ResolveConnectionString(databaseName));
            builder.UseSetting("REDIS_URL", "127.0.0.1:6379");
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIdentityInternalClient>();
                services.AddScoped<IIdentityInternalClient, AllowedIdentityClient>();
            });
        }
    }

    private sealed class StubAlternativeRouteWebApplicationFactory(IMediator mediator) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
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

    private sealed class AllowedIdentityClient : IIdentityInternalClient
    {
        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(IdentityUserLookupResult.ValidationFailure("Unused in this test."));
    }

    private sealed class RecordingMediator(object response) : IMediator
    {
        public int SendCount { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult((TResponse)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult<object?>(response);
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => EmptyStream<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => EmptyStream<object?>();

        private static async IAsyncEnumerable<T> EmptyStream<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
