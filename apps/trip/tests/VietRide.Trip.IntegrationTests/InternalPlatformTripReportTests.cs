using System.Data;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests;

public sealed class InternalPlatformTripReportTests
    : IClassFixture<PlatformTripReportWebApplicationFactory>
{
    private static readonly DateTimeOffset From =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PlatformTripReportWebApplicationFactory _factory;

    public InternalPlatformTripReportTests(PlatformTripReportWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetTrips_UsesCompletedHalfOpenRangeAndReturnsRawGroupedPayload()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetTripsAsync();
        var operatorA = Guid.Parse("40000000-0000-0000-0000-000000000011");
        var operatorB = Guid.Parse("40000000-0000-0000-0000-000000000012");
        await _factory.SeedTripAsync(operatorA, TripStatus.COMPLETED, From);
        await _factory.SeedTripAsync(operatorA, TripStatus.COMPLETED, From.AddDays(4));
        await _factory.SeedTripAsync(operatorA, TripStatus.COMPLETED, To);
        await _factory.SeedTripAsync(operatorA, TripStatus.IN_PROGRESS, From.AddDays(2));
        await _factory.SeedTripAsync(operatorB, TripStatus.COMPLETED, From.AddDays(8));
        await _factory.SeedTripAsync(operatorB, TripStatus.COMPLETED, From.AddTicks(-1));

        using var client = _factory.CreateInternalClient();
        var response = await client.GetAsync(ReportPath(From, To));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.TryGetProperty("success", out _).Should().BeFalse();
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        items.Should().HaveCount(2);
        AssertItem(items[0], operatorA, 2);
        AssertItem(items[1], operatorB, 1);
    }

    [Fact]
    public async Task GetTrips_RequiresInternalJwtAndReturnsCanonicalRangeErrors()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetTripsAsync();
        using var anonymousClient = _factory.CreateClient();

        var unauthorized = await anonymousClient.GetAsync(ReportPath(From, To));

        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(unauthorized, "AUTH_TOKEN_INVALID");

        using var internalClient = _factory.CreateInternalClient();
        var invalidRange = await internalClient.GetAsync(
            "/internal/v1/reports/platform/trips" +
            "?from=2026-07-01T00%3A00%3A00%2B00%3A00" +
            "&to=2026-07-02T00%3A00%3A00Z");

        invalidRange.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(invalidRange, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task ManualAndAutomaticCompletion_EachContributeOneStableReportRow()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetTripsAsync();
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000013");
        var manualTripId = await _factory.SeedInProgressTripAsync(operatorId);
        var automaticTripId = await _factory.SeedInProgressTripAsync(operatorId);

        await _factory.CompleteTripAsync(manualTripId, From.AddDays(10), manual: true);
        await _factory.CompleteTripAsync(automaticTripId, From.AddDays(11), manual: false);

        using var client = _factory.CreateInternalClient();
        var first = await client.GetAsync(ReportPath(From, To));
        var replay = await client.GetAsync(ReportPath(From, To));
        var firstBody = await first.Content.ReadAsByteArrayAsync();
        var replayBody = await replay.Content.ReadAsByteArrayAsync();
        using var firstDocument = JsonDocument.Parse(firstBody);
        using var replayDocument = JsonDocument.Parse(replayBody);
        var firstItem = firstDocument.RootElement.GetProperty("items").EnumerateArray().Single();
        var replayItem = replayDocument.RootElement.GetProperty("items").EnumerateArray().Single();
        AssertItem(firstItem, operatorId, 2);
        AssertItem(replayItem, operatorId, 2);
        (await _factory.CountTripsAsync()).Should().Be(2);
    }

    [Fact]
    public async Task TripStatsMismatch_FailsClosedAndIdempotentBackfillRecovers()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetTripsAsync();
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000014");
        await _factory.SeedTripAsync(operatorId, TripStatus.COMPLETED, From.AddDays(12));
        await _factory.DeletePlatformProjectionAsync();

        using var client = _factory.CreateInternalClient();
        var mismatch = await client.GetAsync(ReportPath(From, To));

        mismatch.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await AssertErrorCodeAsync(mismatch, "UPSTREAM_UNAVAILABLE");

        (await _factory.RunPlatformBackfillTwiceAsync()).Should().Be(1);
        var recovered = await client.GetAsync(ReportPath(From, To));
        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync());
        AssertItem(
            document.RootElement.GetProperty("items").EnumerateArray().Single(),
            operatorId,
            1);
    }

    [Fact]
    public async Task CompletedReportMigration_IsReversibleAndPlannerUsesPartialIndex()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetTripsAsync();

        await _factory.MigrateAsync("20260716142716_AddStationMergeRedirect");
        (await _factory.ReportIndexExistsAsync()).Should().BeFalse();
        await _factory.MigrateAsync();
        (await _factory.ReportIndexExistsAsync()).Should().BeTrue();

        await _factory.SeedPlannerFixtureAsync();
        var plan = await _factory.ExplainReportQueryAsync(
            From.AddDays(3),
            From.AddDays(4));

        plan.Should().Contain("idx_trips_completed_report");
    }

    private static string ReportPath(DateTimeOffset from, DateTimeOffset to)
        => "/internal/v1/reports/platform/trips" +
           $"?from={Uri.EscapeDataString(ToRfc3339Utc(from))}" +
           $"&to={Uri.EscapeDataString(ToRfc3339Utc(to))}";

    private static string ToRfc3339Utc(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static void AssertItem(JsonElement item, Guid operatorId, long completedTripCount)
    {
        item.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        item.GetProperty("completedTripCount").GetInt64().Should().Be(completedTripCount);
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

public sealed class PlatformTripReportWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly string _connectionString = BuildTestConnectionString();
    private bool _databaseCreated;
    private bool _initialized;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Identity:BaseUrl", "http://identity.invalid");
        builder.UseSetting("REDIS_URL", "127.0.0.1:6379,abortConnect=false");
        builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>();
            services.RemoveAll<DbContextOptions<TripDbContext>>();
            services.RemoveAll<TripDbContext>();
            services.RemoveAll<VietRideDbContextBase>();

            services.AddSingleton(_ =>
            {
                var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
                dataSourceBuilder.MapEnum<OutboxEventStatus>(
                    "outbox_event_status",
                    new NpgsqlNullNameTranslator());
                TripDbContext.ConfigurePostgresEnums(dataSourceBuilder);
                return dataSourceBuilder.Build();
            });
            services.AddDbContext<TripDbContext>((provider, options) =>
                options.UseNpgsql(
                    provider.GetRequiredService<NpgsqlDataSource>(),
                    npgsql => npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        TripDbContext.SchemaName))
                    .ConfigureWarnings(warnings => warnings.Ignore(
                        Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
            services.AddScoped<VietRideDbContextBase>(
                provider => provider.GetRequiredService<TripDbContext>());
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            await CreateDatabaseAsync();
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
            await db.Database.MigrateAsync();
            var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            await using var connection = await dataSource.OpenConnectionAsync();
            await connection.ReloadTypesAsync();
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public HttpClient CreateInternalClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Auth", $"Bearer {MintInternalJwt()}");
        return client;
    }

    public async Task ResetTripsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE vietride_trip.trips CASCADE;");
    }

    public async Task<Guid> SeedTripAsync(
        Guid operatorId,
        TripStatus status,
        DateTimeOffset? completedAt)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var trip = await CreateTripGraphAsync(db, operatorId, inProgress: false);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_trip.trips
            SET status = CAST({status.ToString()} AS vietride_trip.trip_status),
                completed_at = {completedAt}
            WHERE id = {trip.Id};
            """);
        return trip.Id;
    }

    public async Task<Guid> SeedInProgressTripAsync(Guid operatorId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var trip = await CreateTripGraphAsync(db, operatorId, inProgress: true);
        return trip.Id;
    }

    public async Task CompleteTripAsync(
        Guid tripId,
        DateTimeOffset completedAt,
        bool manual)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var trip = await db.Trips.SingleAsync(item => item.Id == tripId);
        if (manual)
        {
            trip.CompleteManually(completedAt, trip.DriverUserId);
        }
        else
        {
            trip.CompleteAutomatically(completedAt);
        }

        await db.SaveChangesAsync();
    }

    public async Task<int> CountTripsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        return await db.Trips.CountAsync();
    }

    public async Task DeletePlatformProjectionAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM vietride_trip.platform_trip_stats;");
    }

    public async Task<long> RunPlatformBackfillTwiceAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT vietride_trip.rebuild_platform_trip_stats(); " +
            "SELECT vietride_trip.rebuild_platform_trip_stats();");
        return await db.Database.SqlQueryRaw<long>(
                "SELECT COUNT(*)::bigint AS \"Value\" " +
                "FROM vietride_trip.platform_trip_stats")
            .SingleAsync();
    }

    public async Task MigrateAsync(string? targetMigration = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    public async Task<bool> ReportIndexExistsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        return await db.Database.SqlQueryRaw<bool>(
                "SELECT to_regclass('vietride_trip.idx_trips_completed_report') " +
                "IS NOT NULL AS \"Value\"")
            .SingleAsync();
    }

    public async Task SeedPlannerFixtureAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000099");
        var (routeId, vehicleTypeId) = await CreatePlannerPrerequisitesAsync(db, operatorId);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            CREATE TEMP TABLE report_trip_fixture AS
            SELECT series,
                   gen_random_uuid() AS vehicle_id,
                   gen_random_uuid() AS driver_id
            FROM generate_series(1, 12000) AS series;

            INSERT INTO vietride_trip.vehicles (
                id, operator_id, vehicle_type_id, license_plate,
                seat_layout_json, total_seats)
            SELECT vehicle_id,
                   {operatorId},
                   {vehicleTypeId},
                   'RP' || lpad(series::text, 10, '0'),
                   jsonb_build_object('rows', jsonb_build_array()),
                   20
            FROM report_trip_fixture;

            INSERT INTO vietride_trip.trips (
                id, operator_id, route_id, vehicle_id, seat_layout_snapshot_json, driver_user_id,
                departure_date_time, estimated_arrival_time, completed_at,
                status, source, base_fare)
            SELECT gen_random_uuid(),
                   {operatorId},
                   {routeId},
                   vehicle_id,
                   jsonb_build_object('rows', jsonb_build_array()),
                   driver_id,
                   '2026-01-01T00:00:00Z'::timestamptz + series * interval '1 second',
                   '2026-01-01T04:00:00Z'::timestamptz + series * interval '1 second',
                   CASE WHEN series <= 2000
                       THEN '2026-07-01T00:00:00Z'::timestamptz
                           + ((series - 1) % 30) * interval '1 day'
                       ELSE NULL
                   END,
                   CASE WHEN series <= 2000
                       THEN 'COMPLETED'::vietride_trip.trip_status
                       ELSE 'SCHEDULED'::vietride_trip.trip_status
                   END,
                   'MANUAL'::vietride_trip.trip_source,
                   100000
            FROM report_trip_fixture;

            ANALYZE vietride_trip.trips;
            """);
    }

    public async Task<string> ExplainReportQueryAsync(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            EXPLAIN
            SELECT operator_id, COUNT(*)
            FROM vietride_trip.trips
            WHERE status = 'COMPLETED'::vietride_trip.trip_status
              AND completed_at >= @from_utc
              AND completed_at < @to_utc
            GROUP BY operator_id;
            """;
        command.Parameters.Add(new NpgsqlParameter("from_utc", from));
        command.Parameters.Add(new NpgsqlParameter("to_utc", to));
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_databaseCreated)
        {
            await DropDatabaseAsync();
        }

        _initializationLock.Dispose();
    }

    private static async Task<VietRide.Trip.Domain.Entities.Trip> CreateTripGraphAsync(
        TripDbContext db,
        Guid operatorId,
        bool inProgress)
    {
        var now = DateTimeOffset.UtcNow;
        var origin = Station.Create(
            "Report Origin",
            $"report-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City",
            latitude: 10.7769m,
            longitude: 106.7009m);
        var destination = Station.Create(
            "Report Destination",
            $"report-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Report route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            240);
        var vehicleType = VehicleType.Create(
            $"RPT_{Guid.NewGuid():N}"[..24],
            "Report vehicle type",
            5,
            20);
        using var layout = JsonDocument.Parse("{\"rows\":[]}");
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"RPT-{Guid.NewGuid():N}"[..20],
            layout.RootElement,
            20,
            500m,
            10m);
        var trip = VietRide.Trip.Domain.Entities.Trip.Create(
            operatorId,
            route.Id,
            vehicle.Id,
            Guid.NewGuid(),
            null,
            null,
            now,
            now.AddHours(4),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 5m,
            seatLayoutSnapshotJson: vehicle.SeatLayoutJson);
        if (inProgress)
        {
            trip.MarkBoarding(now.AddMinutes(-10));
            trip.Start(now.AddMinutes(-5));
        }

        db.AddRange(origin, destination, route, vehicleType, vehicle, trip);
        await db.SaveChangesAsync();
        return trip;
    }

    private static async Task<(Guid RouteId, Guid VehicleTypeId)> CreatePlannerPrerequisitesAsync(
        TripDbContext db,
        Guid operatorId)
    {
        var origin = Station.Create(
            "Planner Origin",
            $"planner-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City",
            latitude: 10.7769m,
            longitude: 106.7009m);
        var destination = Station.Create(
            "Planner Destination",
            $"planner-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Planner report route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            240);
        var vehicleType = VehicleType.Create(
            $"PLN_{Guid.NewGuid():N}"[..24],
            "Planner vehicle type",
            5,
            20);
        db.AddRange(origin, destination, route, vehicleType);
        await db.SaveChangesAsync();
        return (route.Id, vehicleType.Id);
    }

    private async Task CreateDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{DatabaseName()}\";";
        await command.ExecuteNonQueryAsync();
        _databaseCreated = true;
    }

    private async Task DropDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName()}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
        _databaseCreated = false;
    }

    private string DatabaseName()
        => new NpgsqlConnectionStringBuilder(_connectionString).Database!;

    private string MaintenanceConnectionString()
        => new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Database = "postgres",
        }.ConnectionString;

    private static string BuildTestConnectionString()
    {
        var configured =
            Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING")
            ?? "Host=127.0.0.1;Port=5432;Database=unused;Username=vietride;Password=vietride_dev";
        return new NpgsqlConnectionStringBuilder(configured)
        {
            Database = $"vietride_trip_platform_report_{Guid.NewGuid():N}",
        }.ConnectionString;
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
                new Dictionary<string, object>
                {
                    ["iss"] = "vietride-gateway",
                    ["aud"] = "vietride-internal",
                    ["sub"] = Guid.NewGuid().ToString(),
                    ["role"] = "SYSTEM_ADMIN",
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
