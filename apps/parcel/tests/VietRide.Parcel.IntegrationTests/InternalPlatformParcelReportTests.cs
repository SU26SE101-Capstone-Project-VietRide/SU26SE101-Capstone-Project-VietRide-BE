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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StackExchange.Redis;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;

namespace VietRide.Parcel.IntegrationTests;

public sealed class InternalPlatformParcelReportTests
    : IClassFixture<PlatformParcelReportWebApplicationFactory>
{
    private static readonly DateTimeOffset From =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PlatformParcelReportWebApplicationFactory _factory;

    public InternalPlatformParcelReportTests(PlatformParcelReportWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetParcels_UsesConfirmedHalfOpenRangeAndPreservesSignedRevenue()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();
        var operatorA = Guid.Parse("40000000-0000-0000-0000-000000000021");
        var operatorB = Guid.Parse("40000000-0000-0000-0000-000000000022");
        await _factory.SeedParcelAsync(
            operatorA,
            ParcelStatus.DELIVERY_CONFIRMED,
            From,
            deposit: 100_000,
            additional: 50_000,
            refund: 20_000);
        await _factory.SeedParcelAsync(
            operatorA,
            ParcelStatus.DELIVERY_CONFIRMED,
            From.AddDays(4),
            deposit: 100_000,
            additional: 0,
            refund: 300_000);
        await _factory.SeedParcelAsync(
            operatorA,
            ParcelStatus.DELIVERY_CONFIRMED,
            To,
            deposit: 900_000,
            additional: 0,
            refund: 0);
        await _factory.SeedParcelAsync(
            operatorA,
            ParcelStatus.DELIVERED_PENDING_CONFIRM,
            From.AddDays(2),
            deposit: 800_000,
            additional: 0,
            refund: 0);
        await _factory.SeedParcelAsync(
            operatorB,
            ParcelStatus.DELIVERY_CONFIRMED,
            From.AddDays(8),
            deposit: 500_000,
            additional: 20_000,
            refund: 10_000);
        await _factory.SeedParcelAsync(
            operatorB,
            ParcelStatus.DELIVERY_CONFIRMED,
            From.AddTicks(-1),
            deposit: 700_000,
            additional: 0,
            refund: 0);

        using var client = _factory.CreateInternalClient();
        var response = await client.GetAsync(ReportPath(From, To));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.TryGetProperty("success", out _).Should().BeFalse();
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        items.Should().HaveCount(2);
        AssertItem(items[0], operatorA, 2, -70_000);
        AssertItem(items[1], operatorB, 1, 510_000);
    }

    [Fact]
    public async Task GetParcels_RequiresInternalJwtAndReturnsCanonicalRangeErrors()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();
        using var anonymousClient = _factory.CreateClient();

        var unauthorized = await anonymousClient.GetAsync(ReportPath(From, To));

        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(unauthorized, "AUTH_TOKEN_INVALID");

        using var internalClient = _factory.CreateInternalClient();
        var invalidRange = await internalClient.GetAsync(
            "/internal/v1/reports/platform/parcels" +
            "?from=2026-07-01T00%3A00%3A00%2B00%3A00" +
            "&to=2026-07-02T00%3A00%3A00Z");

        invalidRange.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(invalidRange, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetParcels_WhenSignedNumericAggregateExceedsInt64_ReturnsOverflow()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000023");
        var perParcelRevenue = (long.MaxValue / 2) + 1;
        await _factory.SeedParcelAsync(
            operatorId,
            ParcelStatus.DELIVERY_CONFIRMED,
            From.AddDays(1),
            deposit: perParcelRevenue,
            additional: 0,
            refund: 0);
        await _factory.SeedParcelAsync(
            operatorId,
            ParcelStatus.DELIVERY_CONFIRMED,
            From.AddDays(1),
            deposit: perParcelRevenue,
            additional: 0,
            refund: 0);

        using var client = _factory.CreateInternalClient();
        var response = await client.GetAsync(ReportPath(From, To));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await AssertErrorCodeAsync(response, "REPORT_VALUE_OVERFLOW");
    }

    [Fact]
    public async Task ParcelStatsMismatch_FailsClosedAndIdempotentBackfillRecovers()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000024");
        await _factory.SeedParcelAsync(
            operatorId,
            ParcelStatus.DELIVERY_CONFIRMED,
            From.AddDays(12),
            deposit: 700_000,
            additional: 50_000,
            refund: 10_000);
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
            1,
            740_000);
    }

    [Fact]
    public async Task ConfirmedReportMigration_IsReversibleAndPlannerUsesPartialIndex()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();

        await _factory.MigrateAsync("20260714113506_PreserveExplicitParcelUpdatedAt");
        (await _factory.ReportIndexExistsAsync()).Should().BeFalse();
        await _factory.MigrateAsync();
        (await _factory.ReportIndexExistsAsync()).Should().BeTrue();

        await _factory.SeedPlannerFixtureAsync();
        var plan = await _factory.ExplainReportQueryAsync(
            From.AddDays(3),
            From.AddDays(4));

        plan.Should().Contain("idx_parcels_confirmed_report");
    }

    private static string ReportPath(DateTimeOffset from, DateTimeOffset to)
        => "/internal/v1/reports/platform/parcels" +
           $"?from={Uri.EscapeDataString(ToRfc3339Utc(from))}" +
           $"&to={Uri.EscapeDataString(ToRfc3339Utc(to))}";

    private static string ToRfc3339Utc(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static void AssertItem(
        JsonElement item,
        Guid operatorId,
        long deliveredParcelCount,
        long parcelRevenueVnd)
    {
        item.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        item.GetProperty("deliveredParcelCount").GetInt64().Should().Be(deliveredParcelCount);
        item.GetProperty("parcelRevenueVnd").GetInt64().Should().Be(parcelRevenueVnd);
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

public sealed class PlatformParcelReportWebApplicationFactory : WebApplicationFactory<Program>
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
        builder.UseSetting("Trip:BaseUrl", "http://trip.invalid");
        builder.UseSetting("Payment:BaseUrl", "http://payment.invalid");
        builder.UseSetting("Booking:BaseUrl", "http://booking.invalid");
        builder.UseSetting("Identity:BaseUrl", "http://identity.invalid");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["Booking:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(InMemoryRedisConnectionMultiplexer.Create());
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
            var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
            await db.Database.MigrateAsync();
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var wasClosed = connection.State != ConnectionState.Open;
            if (wasClosed)
            {
                await connection.OpenAsync();
            }

            await connection.ReloadTypesAsync();
            if (wasClosed)
            {
                await connection.CloseAsync();
            }

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

    public async Task ResetAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE vietride_parcel.parcels CASCADE;");
    }

    public async Task SeedParcelAsync(
        Guid operatorId,
        ParcelStatus status,
        DateTimeOffset? confirmedAt,
        long deposit,
        long additional,
        long refund)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        var id = Guid.NewGuid();
        var code = $"VRP-{Guid.NewGuid():N}"[..24];
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_parcel.parcels (
                id, parcel_code, sender_user_id, recipient_name, recipient_phone,
                operator_id, trip_id, size_category, estimated_size_category, estimated_weight_kg,
                total_price_vnd, deposit_amount, original_deposit_amount,
                additional_amount, refund_amount, status, confirmed_at)
            VALUES (
                {id}, {code}, {Guid.NewGuid()}, 'Recipient', '+84901234567',
                {operatorId}, {Guid.NewGuid()},
                'SMALL'::vietride_parcel.parcel_size_category,
                'SMALL'::vietride_parcel.parcel_size_category, 1,
                {deposit}, {deposit}, {deposit},
                {additional}, {refund},
                CAST({status.ToString()} AS vietride_parcel.parcel_status),
                {confirmedAt});
            """);
    }

    public async Task DeletePlatformProjectionAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM vietride_parcel.platform_parcel_stats;");
    }

    public async Task<long> RunPlatformBackfillTwiceAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT vietride_parcel.rebuild_platform_parcel_stats(); " +
            "SELECT vietride_parcel.rebuild_platform_parcel_stats();");
        return await db.Database.SqlQueryRaw<long>(
                "SELECT COUNT(*)::bigint AS \"Value\" " +
                "FROM vietride_parcel.platform_parcel_stats")
            .SingleAsync();
    }

    public async Task MigrateAsync(string? targetMigration = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    public async Task<bool> ReportIndexExistsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        return await db.Database.SqlQueryRaw<bool>(
                "SELECT to_regclass('vietride_parcel.idx_parcels_confirmed_report') " +
                "IS NOT NULL AS \"Value\"")
            .SingleAsync();
    }

    public async Task SeedPlannerFixtureAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO vietride_parcel.parcels (
                id, parcel_code, sender_user_id, recipient_name, recipient_phone,
                operator_id, trip_id, size_category, estimated_size_category, estimated_weight_kg,
                total_price_vnd, deposit_amount, original_deposit_amount,
                additional_amount, refund_amount, status, confirmed_at)
            SELECT gen_random_uuid(),
                   'VRP-20260717-' || lpad(series::text, 8, '0'),
                   gen_random_uuid(),
                   'Recipient',
                   '+84901234567',
                   '40000000-0000-0000-0000-000000000099'::uuid,
                   gen_random_uuid(),
                   'SMALL'::vietride_parcel.parcel_size_category,
                   'SMALL'::vietride_parcel.parcel_size_category,
                   1,
                   100000,
                   100000,
                   100000,
                   0,
                   0,
                   CASE WHEN series <= 2000
                       THEN 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
                       ELSE 'PENDING'::vietride_parcel.parcel_status
                   END,
                   CASE WHEN series <= 2000
                       THEN '2026-07-01T00:00:00Z'::timestamptz
                           + ((series - 1) % 30) * interval '1 day'
                       ELSE NULL
                   END
            FROM generate_series(1, 32000) AS series;
            ANALYZE vietride_parcel.parcels;
            """);
    }

    public async Task<string> ExplainReportQueryAsync(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            EXPLAIN
            SELECT operator_id,
                   COUNT(*)::numeric,
                   SUM(deposit_amount::numeric + additional_amount::numeric - refund_amount::numeric)
            FROM vietride_parcel.parcels
            WHERE status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
              AND confirmed_at >= @from_utc
              AND confirmed_at < @to_utc
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

    private async Task CreateDatabaseAsync()
    {
        if (_databaseCreated)
        {
            return;
        }

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
            Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=unused;Username=vietride;Password=vietride_dev";
        return new NpgsqlConnectionStringBuilder(configured)
        {
            Database = $"vietride_parcel_platform_report_{Guid.NewGuid():N}",
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
