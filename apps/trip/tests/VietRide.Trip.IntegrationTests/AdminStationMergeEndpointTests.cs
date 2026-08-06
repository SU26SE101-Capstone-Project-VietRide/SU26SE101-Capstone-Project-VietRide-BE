using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
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
using StackExchange.Redis;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.DependencyInjection;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests;

public sealed class AdminStationMergeEndpointTests
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task NormalizeAndMerge_RelinksFullGraphPersistsOutboxAndReplaysResponse()
    {
        var databaseName = $"vietride_trip_station_merge_endpoint_{Guid.NewGuid():N}";
        var idempotencyKeys = new List<string>();
        using var factory = new StationWebApplicationFactory(databaseName);
        try
        {
            var seed = await InitializeAndSeedMergeGraphAsync(factory);
            using var client = factory.CreateClient();
            var adminId = Guid.NewGuid();

            var patchKey = NewKey(idempotencyKeys);
            using var patchRequest = CreateAdminRequest(
                HttpMethod.Patch,
                $"/v1/admin/stations/{seed.PrimaryId}",
                adminId,
                patchKey,
                JsonContent.Create(new
                {
                    name = "Primary Normalized",
                    city = "Primary City",
                    ward = "Primary Ward",
                    supportsShuttle = false,
                }));
            using var patchResponse = await client.SendAsync(patchRequest);
            patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var mergeKey = NewKey(idempotencyKeys);
            using var firstRequest = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{seed.PrimaryId}/merge",
                adminId,
                mergeKey,
                JsonContent.Create(new { duplicateId = seed.DuplicateId }));
            using var firstResponse = await client.SendAsync(firstRequest);
            var firstBody = await firstResponse.Content.ReadAsByteArrayAsync();
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var document = JsonDocument.Parse(firstBody))
            {
                var data = document.RootElement.GetProperty("data");
                data.GetProperty("primaryStation").GetProperty("name").GetString()
                    .Should().Be("Primary Normalized");
                data.GetProperty("primaryStation").GetProperty("addressStreet").GetString()
                    .Should().Be("12 Duplicate Street");
                data.GetProperty("primaryStation").GetProperty("supportsShuttle").GetBoolean()
                    .Should().BeTrue();
                var counts = data.GetProperty("relinkedCounts");
                counts.GetProperty("operatorMappings").GetInt32().Should().Be(1);
                counts.GetProperty("collapsedOperatorMappings").GetInt32().Should().Be(1);
                counts.GetProperty("routeOrigins").GetInt32().Should().Be(1);
                counts.GetProperty("routeDestinations").GetInt32().Should().Be(1);
                counts.GetProperty("alternativeRoutes").GetInt32().Should().Be(1);
                counts.GetProperty("shuttleTrips").GetInt32().Should().Be(1);
                counts.GetProperty("flattenedRedirects").GetInt32().Should().Be(1);
            }

            using var replayRequest = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{seed.PrimaryId}/merge",
                adminId,
                mergeKey,
                JsonContent.Create(new { duplicateId = seed.DuplicateId }));
            using var replayResponse = await client.SendAsync(replayRequest);
            replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            (await replayResponse.Content.ReadAsByteArrayAsync()).Should().Equal(firstBody);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
            db.ChangeTracker.Clear();
            var primary = await db.Stations.AsNoTracking().SingleAsync(station => station.Id == seed.PrimaryId);
            var duplicate = await db.Stations.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(station => station.Id == seed.DuplicateId);
            var redirect = await db.Stations.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(station => station.Id == seed.OldRedirectId);
            primary.AddressStreet.Should().Be("12 Duplicate Street");
            primary.ContactPhone.Should().Be("0900000001");
            primary.ContactEmail.Should().Be("duplicate@example.com");
            primary.SupportsShuttle.Should().BeTrue();
            duplicate.MergedIntoStationId.Should().Be(primary.Id);
            duplicate.DeletedAt.Should().NotBeNull();
            redirect.MergedIntoStationId.Should().Be(primary.Id);
            (await db.OperatorStations.CountAsync(mapping => mapping.StationId == primary.Id)).Should().Be(2);
            (await db.OperatorStations.CountAsync(mapping => mapping.StationId == duplicate.Id)).Should().Be(0);
            (await db.Routes.SingleAsync(route => route.Id == seed.OriginRouteId)).OriginStationId
                .Should().Be(primary.Id);
            (await db.Routes.SingleAsync(route => route.Id == seed.DestinationRouteId)).DestinationStationId
                .Should().Be(primary.Id);
            (await db.AlternativeRoutes.SingleAsync(route => route.Id == seed.AlternativeRouteId)).DestinationStationId
                .Should().Be(primary.Id);
            (await db.ShuttleTrips.SingleAsync(trip => trip.Id == seed.ShuttleTripId)).StationId
                .Should().Be(primary.Id);

            var outbox = await db.OutboxEvents.AsNoTracking().OrderBy(row => row.CreatedAt).ToArrayAsync();
            outbox.Should().HaveCount(2);
            outbox.Count(row => row.EventType == "trip.station.normalized").Should().Be(1);
            outbox.Count(row => row.EventType == "trip.station.merged").Should().Be(1);
            outbox.Should().OnlyContain(row =>
                !row.Payload.Contains("contactPhone", StringComparison.Ordinal)
                && !row.Payload.Contains("contactEmail", StringComparison.Ordinal));
        }
        finally
        {
            await DeleteIdempotencyKeysAsync(idempotencyKeys);
            await DeleteDatabaseAsync(factory);
        }
    }

    [Fact]
    public async Task Merge_MissingAndMismatchedIdempotencyKeyHaveNoExtraSideEffects()
    {
        var databaseName = $"vietride_trip_station_idempotency_{Guid.NewGuid():N}";
        var idempotencyKeys = new List<string>();
        using var factory = new StationWebApplicationFactory(databaseName);
        try
        {
            await InitializeAsync(factory);
            var primary = Station.Create("Primary", $"primary-{Guid.NewGuid():N}", "City", "Province");
            var duplicateOne = Station.Create("Duplicate One", $"duplicate-one-{Guid.NewGuid():N}", "City", "Province");
            var duplicateTwo = Station.Create("Duplicate Two", $"duplicate-two-{Guid.NewGuid():N}", "City", "Province");
            await SeedAsync(factory, primary, duplicateOne, duplicateTwo);
            using var client = factory.CreateClient();
            var adminId = Guid.NewGuid();

            using var missingKeyRequest = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{primary.Id}/merge",
                adminId,
                null,
                JsonContent.Create(new { duplicateId = duplicateOne.Id }));
            using var missingKeyResponse = await client.SendAsync(missingKeyRequest);
            missingKeyResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            await AssertErrorCodeAsync(missingKeyResponse, "IDEMPOTENCY_KEY_REQUIRED");

            var forbiddenKey = NewKey(idempotencyKeys);
            using var forbiddenRequest = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{primary.Id}/merge",
                adminId,
                forbiddenKey,
                JsonContent.Create(new { duplicateId = duplicateOne.Id }),
                "OPERATOR_ADMIN");
            using var forbiddenResponse = await client.SendAsync(forbiddenRequest);
            forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var selfMergeKey = NewKey(idempotencyKeys);
            using var selfMergeRequest = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{primary.Id}/merge",
                adminId,
                selfMergeKey,
                JsonContent.Create(new { duplicateId = primary.Id }));
            using var selfMergeResponse = await client.SendAsync(selfMergeRequest);
            selfMergeResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            await AssertErrorCodeAsync(selfMergeResponse, "VALIDATION_ERROR");

            var key = NewKey(idempotencyKeys);
            using var firstRequest = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{primary.Id}/merge",
                adminId,
                key,
                JsonContent.Create(new { duplicateId = duplicateOne.Id }));
            using var firstResponse = await client.SendAsync(firstRequest);
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var mismatchRequest = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{primary.Id}/merge",
                adminId,
                key,
                JsonContent.Create(new { duplicateId = duplicateTwo.Id }));
            using var mismatchResponse = await client.SendAsync(mismatchRequest);
            mismatchResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            await AssertErrorCodeAsync(mismatchResponse, "IDEMPOTENCY_KEY_MISMATCH");

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
            var stillCanonical = await db.Stations.AsNoTracking().SingleAsync(station => station.Id == duplicateTwo.Id);
            stillCanonical.MergedIntoStationId.Should().BeNull();
            (await db.OutboxEvents.CountAsync(row => row.EventType == "trip.station.merged")).Should().Be(1);
        }
        finally
        {
            await DeleteIdempotencyKeysAsync(idempotencyKeys);
            await DeleteDatabaseAsync(factory);
        }
    }

    [Fact]
    public async Task Merge_RouteConflictReturnsExact409AndRollsBackEverything()
    {
        var databaseName = $"vietride_trip_station_conflict_endpoint_{Guid.NewGuid():N}";
        var idempotencyKeys = new List<string>();
        using var factory = new StationWebApplicationFactory(databaseName);
        try
        {
            await InitializeAsync(factory);
            var primary = Station.Create("Primary", $"primary-{Guid.NewGuid():N}", "City", "Province");
            var duplicate = Station.Create(
                "Duplicate",
                $"duplicate-{Guid.NewGuid():N}",
                "City",
                "Province",
                addressStreet: "Must not copy");
            var route = Route.Create(
                Guid.NewGuid(),
                "Conflict",
                primary.Id,
                duplicate.Id,
                Money.FromRaw(100_000),
                null,
                null);
            await SeedAsync(factory, primary, duplicate, route);
            using var client = factory.CreateClient();
            var key = NewKey(idempotencyKeys);

            using var request = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{primary.Id}/merge",
                Guid.NewGuid(),
                key,
                JsonContent.Create(new { duplicateId = duplicate.Id }));
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            await AssertErrorCodeAsync(response, "STATION_MERGE_CONFLICT");

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
            db.ChangeTracker.Clear();
            (await db.Stations.SingleAsync(station => station.Id == primary.Id)).AddressStreet.Should().BeNull();
            var persistedDuplicate = await db.Stations.SingleAsync(station => station.Id == duplicate.Id);
            persistedDuplicate.DeletedAt.Should().BeNull();
            persistedDuplicate.MergedIntoStationId.Should().BeNull();
            (await db.Routes.SingleAsync(candidate => candidate.Id == route.Id)).DestinationStationId
                .Should().Be(duplicate.Id);
            (await db.OutboxEvents.CountAsync(row => row.EventType == "trip.station.merged")).Should().Be(0);
        }
        finally
        {
            await DeleteIdempotencyKeysAsync(idempotencyKeys);
            await DeleteDatabaseAsync(factory);
        }
    }

    [Fact]
    public async Task ConcurrentMergesSharingDuplicateProduceOneCanonicalOutcome()
    {
        var databaseName = $"vietride_trip_station_concurrent_merge_{Guid.NewGuid():N}";
        var idempotencyKeys = new List<string>();
        using var factory = new StationWebApplicationFactory(databaseName);
        try
        {
            await InitializeAsync(factory);
            var primaryOne = Station.Create("Primary One", $"primary-one-{Guid.NewGuid():N}", "City", "Province");
            var primaryTwo = Station.Create("Primary Two", $"primary-two-{Guid.NewGuid():N}", "City", "Province");
            var duplicate = Station.Create("Duplicate", $"duplicate-{Guid.NewGuid():N}", "City", "Province");
            await SeedAsync(factory, primaryOne, primaryTwo, duplicate);
            using var client = factory.CreateClient();
            var adminId = Guid.NewGuid();
            var firstKey = NewKey(idempotencyKeys);
            var secondKey = NewKey(idempotencyKeys);

            using var firstRequest = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{primaryOne.Id}/merge",
                adminId,
                firstKey,
                JsonContent.Create(new { duplicateId = duplicate.Id }));
            using var secondRequest = CreateAdminRequest(
                HttpMethod.Post,
                $"/v1/admin/stations/{primaryTwo.Id}/merge",
                adminId,
                secondKey,
                JsonContent.Create(new { duplicateId = duplicate.Id }));
            var responses = await Task.WhenAll(client.SendAsync(firstRequest), client.SendAsync(secondRequest));
            try
            {
                responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
                responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
                await AssertErrorCodeAsync(
                    responses.Single(response => response.StatusCode == HttpStatusCode.Conflict),
                    "STATION_MERGE_CONFLICT");

                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
                var persistedDuplicate = await db.Stations.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(station => station.Id == duplicate.Id);
                var canonicalTarget = persistedDuplicate.MergedIntoStationId;
                canonicalTarget.Should().NotBeNull();
                new[] { primaryOne.Id, primaryTwo.Id }.Should().Contain(canonicalTarget.GetValueOrDefault());
                persistedDuplicate.DeletedAt.Should().NotBeNull();
                (await db.OutboxEvents.CountAsync(row => row.EventType == "trip.station.merged")).Should().Be(1);
            }
            finally
            {
                foreach (var response in responses)
                    response.Dispose();
            }
        }
        finally
        {
            await DeleteIdempotencyKeysAsync(idempotencyKeys);
            await DeleteDatabaseAsync(factory);
        }
    }

    private static async Task<MergeSeed> InitializeAndSeedMergeGraphAsync(StationWebApplicationFactory factory)
    {
        await InitializeAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var operatorOne = Guid.NewGuid();
        var operatorTwo = Guid.NewGuid();
        var primary = Station.Create(
            "Primary",
            $"primary-{Guid.NewGuid():N}",
            "Primary City",
            "Primary Province",
            contactPhone: "0900000001");
        var duplicate = Station.Create(
            "Duplicate",
            $"duplicate-{Guid.NewGuid():N}",
            "Duplicate City",
            "Duplicate Province",
            addressStreet: "12 Duplicate Street",
            latitude: 10.7m,
            longitude: 106.7m,
            contactEmail: "duplicate@example.com",
            operatingHours: "{\"mon\":\"06:00-22:00\"}",
            facilities: "[\"parking\"]",
            supportsShuttle: true);
        var other = Station.Create("Other", $"other-{Guid.NewGuid():N}", "Other City", "Other Province");
        var oldRedirect = Station.Create("Old Redirect", $"old-{Guid.NewGuid():N}", "Old City", "Old Province");
        oldRedirect.MarkMergedInto(duplicate.Id, DateTimeOffset.UtcNow.AddDays(-1));
        var primaryMapping = OperatorStation.Create(operatorOne, primary.Id, contactPhone: "0900000001");
        primaryMapping.Deactivate();
        var duplicateCollision = OperatorStation.Create(
            operatorOne,
            duplicate.Id,
            displayNameOverride: "Duplicate Counter",
            counterLocation: "Gate 2");
        var duplicateRelink = OperatorStation.Create(operatorTwo, duplicate.Id, displayNameOverride: "Operator Two");
        var originRoute = Route.Create(
            operatorOne,
            "Duplicate to Other",
            duplicate.Id,
            other.Id,
            Money.FromRaw(100_000),
            null,
            null);
        var destinationRoute = Route.Create(
            operatorOne,
            "Other to Duplicate",
            other.Id,
            duplicate.Id,
            Money.FromRaw(100_000),
            null,
            null);
        var alternative = AlternativeRoute.Create(originRoute.Id, "Alternative", duplicate.Id, null, null);
        var vehicleType = VehicleType.Create("STATION_ENDPOINT", "Station endpoint vehicle", 5, 20);
        using var layout = JsonDocument.Parse("{\"rows\":[]}");
        var vehicle = Vehicle.Create(
            operatorOne,
            vehicleType.Id,
            $"MERGE-{Guid.NewGuid():N}"[..20],
            layout.RootElement,
            20,
            500m,
            10m);
        var departure = DateTimeOffset.UtcNow.AddHours(2);
        var mainTrip = Domain.Entities.Trip.Create(
            operatorOne,
            originRoute.Id,
            vehicle.Id,
            Guid.NewGuid(),
            null,
            null,
            departure,
            departure.AddHours(3),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            5m);
        var shuttle = ShuttleTrip.Create(
            operatorOne,
            mainTrip.Id,
            duplicate.Id,
            Guid.NewGuid(),
            vehicle.Id,
            departure.AddHours(-1),
            departure.AddMinutes(-30),
            null);
        db.AddRange(
            primary,
            duplicate,
            other,
            oldRedirect,
            primaryMapping,
            duplicateCollision,
            duplicateRelink,
            originRoute,
            destinationRoute,
            alternative,
            vehicleType,
            vehicle,
            mainTrip,
            shuttle);
        await db.SaveChangesAsync();
        return new MergeSeed(
            primary.Id,
            duplicate.Id,
            oldRedirect.Id,
            originRoute.Id,
            destinationRoute.Id,
            alternative.Id,
            shuttle.Id);
    }

    private static async Task InitializeAsync(StationWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TripDbContext>().MigrateAndReloadTypesAsync();
    }

    private static async Task SeedAsync(StationWebApplicationFactory factory, params object[] entities)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static HttpRequestMessage CreateAdminRequest(
        HttpMethod method,
        string path,
        Guid adminId,
        string? idempotencyKey,
        HttpContent? content,
        string role = "SYSTEM_ADMIN")
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(adminId, role)}");
        if (idempotencyKey is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static string CreateInternalJwt(Guid adminId, string role)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim("sub", adminId.ToString()),
                new Claim(ClaimTypes.Role, role),
            ],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedCode);
    }

    private static string NewKey(ICollection<string> keys)
    {
        var key = Guid.NewGuid().ToString("D");
        keys.Add(key);
        return key;
    }

    private static async Task DeleteIdempotencyKeysAsync(IEnumerable<string> keys)
    {
        var values = keys.ToArray();
        if (values.Length == 0)
            return;

        var connectionString = Environment.GetEnvironmentVariable("VIETRIDE_TEST_REDIS")
            ?? "127.0.0.1:6379,abortConnect=false,connectTimeout=3000";
        await using var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var database = redis.GetDatabase();
        foreach (var key in values)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            await database.KeyDeleteAsync([
                $"trip:idem:{key}",
                $"trip:idem:v2:response:{hash}",
                $"trip:idem:v2:processing:{hash}",
            ]);
        }
    }

    private static async Task DeleteDatabaseAsync(StationWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TripDbContext>().Database.EnsureDeletedAsync();
    }

    private sealed class StationWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName;

        public StationWebApplicationFactory(string databaseName) => _databaseName = databaseName;

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

    private sealed record MergeSeed(
        Guid PrimaryId,
        Guid DuplicateId,
        Guid OldRedirectId,
        Guid OriginRouteId,
        Guid DestinationRouteId,
        Guid AlternativeRouteId,
        Guid ShuttleTripId);
}
