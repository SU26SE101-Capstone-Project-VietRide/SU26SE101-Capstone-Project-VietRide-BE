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
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests;

public sealed class InternalStationResolutionTests
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task InternalLookup_DistinguishesCanonicalMergedOrdinaryDeletedAndMissing()
    {
        var databaseName = $"vietride_trip_station_resolution_{Guid.NewGuid():N}";
        using var factory = new StationResolutionWebApplicationFactory(databaseName);
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
                await db.MigrateAndReloadTypesAsync();
                var canonical = Station.Create(
                    "Canonical",
                    $"canonical-{Guid.NewGuid():N}",
                    "City",
                    "Province",
                    latitude: 10.7m,
                    longitude: 106.7m,
                    supportsShuttle: true);
                var merged = Station.Create("Merged Original", $"merged-{Guid.NewGuid():N}", "Old City", "Old Province");
                merged.MarkMergedInto(canonical.Id, DateTimeOffset.UtcNow);
                var ordinaryDeleted = Station.Create("Deleted", $"deleted-{Guid.NewGuid():N}", "City", "Province");
                ordinaryDeleted.SoftDelete(DateTimeOffset.UtcNow);
                db.AddRange(canonical, merged, ordinaryDeleted);
                await db.SaveChangesAsync();
                TestIds = new ResolutionIds(canonical.Id, merged.Id, ordinaryDeleted.Id);
            }

            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Internal-Auth",
                $"Bearer {CreateInternalJwt()}");

            using var canonicalResponse = await client.GetAsync($"/internal/v1/stations/{TestIds.CanonicalId}");
            canonicalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var document = JsonDocument.Parse(await canonicalResponse.Content.ReadAsStringAsync()))
            {
                document.RootElement.TryGetProperty("success", out _).Should().BeFalse();
                document.RootElement.GetProperty("id").GetGuid().Should().Be(TestIds.CanonicalId);
                document.RootElement.GetProperty("supportsShuttle").GetBoolean().Should().BeTrue();
                document.RootElement.GetProperty("isMerged").GetBoolean().Should().BeFalse();
                document.RootElement.GetProperty("canonicalStationId").GetGuid().Should().Be(TestIds.CanonicalId);
            }

            using var mergedResponse = await client.GetAsync($"/internal/v1/stations/{TestIds.MergedId}");
            mergedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var document = JsonDocument.Parse(await mergedResponse.Content.ReadAsStringAsync()))
            {
                document.RootElement.GetProperty("id").GetGuid().Should().Be(TestIds.MergedId);
                document.RootElement.GetProperty("name").GetString().Should().Be("Merged Original");
                document.RootElement.GetProperty("isMerged").GetBoolean().Should().BeTrue();
                document.RootElement.GetProperty("canonicalStationId").GetGuid().Should().Be(TestIds.CanonicalId);
            }

            using var deletedResponse = await client.GetAsync($"/internal/v1/stations/{TestIds.OrdinaryDeletedId}");
            deletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            await AssertErrorCodeAsync(deletedResponse, "STATION_NOT_FOUND");

            using var missingResponse = await client.GetAsync($"/internal/v1/stations/{Guid.NewGuid()}");
            missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            await AssertErrorCodeAsync(missingResponse, "STATION_NOT_FOUND");
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TripDbContext>().Database.EnsureDeletedAsync();
        }
    }

    private ResolutionIds TestIds { get; set; } = null!;

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedCode);
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

    private sealed class StationResolutionWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName;

        public StationResolutionWebApplicationFactory(string databaseName) => _databaseName = databaseName;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting("ConnectionStrings:Default", CreateConnectionString(_databaseName));
            builder.UseSetting("REDIS_URL", "127.0.0.1:6379,abortConnect=false,connectTimeout=3000");
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
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
            template = fallback;
        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private sealed record ResolutionIds(Guid CanonicalId, Guid MergedId, Guid OrdinaryDeletedId);
}
