using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using RouteEntity = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.IntegrationTests;

public sealed class InternalRouteOwnershipEndpointTests
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task ActiveOwnedRoute_ReturnsRawOwnershipDto_WhileCrossOperatorReturnsNotFound()
    {
        await using var fixture = await OwnershipFixture.CreateAsync();
        using var client = fixture.CreateAuthenticatedClient();

        using var owned = await client.GetAsync(
            $"/internal/v1/routes/{fixture.ActiveRouteId:D}/ownership?operatorId={fixture.OperatorAId:D}");

        owned.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var document = JsonDocument.Parse(await owned.Content.ReadAsStringAsync()))
        {
            document.RootElement.TryGetProperty("success", out _).Should().BeFalse();
            document.RootElement.GetProperty("routeId").GetGuid().Should().Be(fixture.ActiveRouteId);
            document.RootElement.GetProperty("operatorId").GetGuid().Should().Be(fixture.OperatorAId);
        }

        using var foreign = await client.GetAsync(
            $"/internal/v1/routes/{fixture.ActiveRouteId:D}/ownership?operatorId={fixture.OperatorBId:D}");
        await AssertRouteNotFoundAsync(foreign);
    }

    [Fact]
    public async Task InactiveDeletedAndMissingRoutes_ReturnRouteNotFound()
    {
        await using var fixture = await OwnershipFixture.CreateAsync();
        using var client = fixture.CreateAuthenticatedClient();

        foreach (var routeId in new[] { fixture.InactiveRouteId, fixture.DeletedRouteId, Guid.NewGuid() })
        {
            using var response = await client.GetAsync(
                $"/internal/v1/routes/{routeId:D}/ownership?operatorId={fixture.OperatorAId:D}");
            await AssertRouteNotFoundAsync(response);
        }
    }

    [Fact]
    public async Task MissingOrTamperedInternalJwt_ReturnsUnauthorized()
    {
        await using var fixture = await OwnershipFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        using var missing = await client.GetAsync(
            $"/internal/v1/routes/{fixture.ActiveRouteId:D}/ownership?operatorId={fixture.OperatorAId:D}");
        missing.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/internal/v1/routes/{fixture.ActiveRouteId:D}/ownership?operatorId={fixture.OperatorAId:D}");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer tampered-token");
        using var tampered = await client.SendAsync(request);
        tampered.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task AssertRouteNotFoundAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("ROUTE_NOT_FOUND");
    }

    private static string CreateInternalJwt()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "INTERNAL_SERVICE"),
            ],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class OwnershipFixture : IAsyncDisposable
    {
        private OwnershipFixture(OwnershipWebApplicationFactory factory)
        {
            Factory = factory;
        }

        public OwnershipWebApplicationFactory Factory { get; }
        public Guid OperatorAId { get; private set; }
        public Guid OperatorBId { get; private set; }
        public Guid ActiveRouteId { get; private set; }
        public Guid InactiveRouteId { get; private set; }
        public Guid DeletedRouteId { get; private set; }

        public static async Task<OwnershipFixture> CreateAsync()
        {
            var databaseName = $"vietride_trip_route_ownership_{Guid.NewGuid():N}";
            var fixture = new OwnershipFixture(new OwnershipWebApplicationFactory(databaseName));
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
            await db.MigrateAndReloadTypesAsync();

            fixture.OperatorAId = Guid.NewGuid();
            fixture.OperatorBId = Guid.NewGuid();
            var origin = Station.Create("Origin", $"origin-{Guid.NewGuid():N}", "HCM", "HCM");
            var destination = Station.Create("Destination", $"destination-{Guid.NewGuid():N}", "Da Nang", "Da Nang");
            var active = CreateRoute(fixture.OperatorAId, "Active", origin.Id, destination.Id);
            var inactive = CreateRoute(fixture.OperatorAId, "Inactive", origin.Id, destination.Id);
            inactive.Deactivate();
            var deleted = CreateRoute(fixture.OperatorAId, "Deleted", origin.Id, destination.Id);
            deleted.SoftDelete(DateTimeOffset.UtcNow);

            db.AddRange(origin, destination, active, inactive, deleted);
            await db.SaveChangesAsync();
            fixture.ActiveRouteId = active.Id;
            fixture.InactiveRouteId = inactive.Id;
            fixture.DeletedRouteId = deleted.Id;
            return fixture;
        }

        public HttpClient CreateAuthenticatedClient()
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt()}");
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TripDbContext>().Database.EnsureDeletedAsync();
            await Factory.DisposeAsync();
        }

        private static RouteEntity CreateRoute(Guid operatorId, string name, Guid originId, Guid destinationId)
            => RouteEntity.Create(operatorId, name, originId, destinationId, Money.FromRaw(100_000), 100m, 120);
    }

    private sealed class OwnershipWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName;

        public OwnershipWebApplicationFactory(string databaseName) => _databaseName = databaseName;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting("ConnectionStrings:Default", CreateConnectionString(_databaseName));
            builder.UseSetting("REDIS_URL", "localhost:6379,abortConnect=false,connectTimeout=3000");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TripDbContext>>();
                services.AddDbContext<TripDbContext>((serviceProvider, options) =>
                    options
                        .UseNpgsql(
                            serviceProvider.GetRequiredService<Npgsql.NpgsqlDataSource>(),
                            npgsql => npgsql.MigrationsHistoryTable(
                                "__ef_migrations_history",
                                TripDbContext.SchemaName))
                        .ConfigureWarnings(warnings =>
                            warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
            });
        }

        private static string CreateConnectionString(string databaseName)
        {
            const string fallback = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
            var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(template))
                template = fallback;
            return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
                ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
                : template;
        }
    }
}
