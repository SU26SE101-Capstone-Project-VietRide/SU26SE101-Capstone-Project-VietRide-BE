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
using StackExchange.Redis;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.TripEvents;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence;

namespace VietRide.Booking.IntegrationTests;

public sealed class InternalPlatformBookingReportTests
    : IClassFixture<PlatformBookingReportWebApplicationFactory>
{
    private static readonly DateTimeOffset From =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PlatformBookingReportWebApplicationFactory _factory;

    public InternalPlatformBookingReportTests(
        PlatformBookingReportWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetBookings_UsesCompletedHalfOpenRangeAndReturnsRawGroupedPayload()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();
        var operatorA = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var operatorB = Guid.Parse("40000000-0000-0000-0000-000000000002");
        await _factory.SeedBookingAsync(operatorA, BookingStatus.COMPLETED, From, 100_000);
        await _factory.SeedBookingAsync(operatorA, BookingStatus.COMPLETED, From.AddDays(4), 250_000);
        await _factory.SeedBookingAsync(operatorA, BookingStatus.COMPLETED, To, 900_000);
        await _factory.SeedBookingAsync(operatorA, BookingStatus.CONFIRMED, From.AddDays(2), 800_000);
        await _factory.SeedBookingAsync(operatorB, BookingStatus.COMPLETED, From.AddDays(8), 500_000);
        await _factory.SeedBookingAsync(operatorB, BookingStatus.COMPLETED, From.AddTicks(-1), 700_000);

        using var client = _factory.CreateInternalClient();
        var response = await client.GetAsync(ReportPath(From, To));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.TryGetProperty("success", out _).Should().BeFalse(
            because: "successful internal responses must remain raw DTOs");
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        items.Should().HaveCount(2);
        AssertItem(items[0], operatorA, 2, 350_000);
        AssertItem(items[1], operatorB, 1, 500_000);
    }

    [Fact]
    public async Task GetBookings_RequiresInternalJwtAndReturnsCanonicalRangeErrors()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();
        using var anonymousClient = _factory.CreateClient();

        var unauthorized = await anonymousClient.GetAsync(ReportPath(From, To));

        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(unauthorized, "AUTH_TOKEN_INVALID");

        using var internalClient = _factory.CreateInternalClient();
        var invalidRange = await internalClient.GetAsync(
            "/internal/v1/reports/platform/bookings" +
            "?from=2026-07-01T00%3A00%3A00%2B00%3A00" +
            "&to=2026-07-02T00%3A00%3A00Z");

        invalidRange.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(invalidRange, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetBookings_WhenNumericSumExceedsInt64_ReturnsCanonicalOverflow()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000003");
        await _factory.SeedBookingAsync(
            operatorId,
            BookingStatus.COMPLETED,
            From.AddDays(1),
            long.MaxValue);
        await _factory.SeedBookingAsync(
            operatorId,
            BookingStatus.COMPLETED,
            From.AddDays(2),
            long.MaxValue);

        using var client = _factory.CreateInternalClient();
        var response = await client.GetAsync(ReportPath(From, To));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await AssertErrorCodeAsync(response, "REPORT_VALUE_OVERFLOW");
    }

    [Fact]
    public async Task TripCompletedLifecycle_IncreasesReportOnceAndReplayDoesNotDoubleCount()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var tripId = Guid.Parse("40000000-0000-0000-0000-000000000040");
        await _factory.SeedBookingAsync(
            operatorId,
            BookingStatus.CONFIRMED,
            completedAt: null,
            totalAmount: 420_000,
            tripId);

        var (firstTransitionCount, replayTransitionCount) =
            await _factory.CompleteTripTwiceAsync(tripId, From.AddDays(10));

        firstTransitionCount.Should().Be(1);
        replayTransitionCount.Should().Be(0);
        using var client = _factory.CreateInternalClient();
        var response = await client.GetAsync(ReportPath(From, To));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = document.RootElement.GetProperty("items").EnumerateArray().Single();
        AssertItem(item, operatorId, 1, 420_000);
        (await _factory.CountCompletionHistoryAsync()).Should().Be(1);
    }

    [Fact]
    public async Task BookingStatsMismatch_FailsClosedAndIdempotentBackfillRecovers()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000005");
        await _factory.SeedBookingAsync(
            operatorId,
            BookingStatus.COMPLETED,
            From.AddDays(12),
            610_000);
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
            610_000);
    }

    [Fact]
    public async Task CompletedReportMigration_IsReversibleAndPlannerUsesPartialIndex()
    {
        await _factory.InitializeDatabaseAsync();
        await _factory.ResetAsync();

        await _factory.MigrateAsync("20260716165252_AddBookingStationRedirects");
        (await _factory.ReportIndexExistsAsync()).Should().BeFalse();
        await _factory.MigrateAsync();
        (await _factory.ReportIndexExistsAsync()).Should().BeTrue();

        await _factory.SeedPlannerFixtureAsync();
        var plan = await _factory.ExplainReportQueryAsync(
            From.AddDays(3),
            From.AddDays(4));

        plan.Should().Contain("idx_bookings_completed_report");
    }

    private static string ReportPath(DateTimeOffset from, DateTimeOffset to)
        => "/internal/v1/reports/platform/bookings" +
           $"?from={Uri.EscapeDataString(ToRfc3339Utc(from))}" +
           $"&to={Uri.EscapeDataString(ToRfc3339Utc(to))}";

    private static string ToRfc3339Utc(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static void AssertItem(
        JsonElement item,
        Guid operatorId,
        long completedBookingCount,
        long bookingRevenueVnd)
    {
        item.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        item.GetProperty("completedBookingCount").GetInt64().Should().Be(completedBookingCount);
        item.GetProperty("bookingRevenueVnd").GetInt64().Should().Be(bookingRevenueVnd);
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

public sealed class PlatformBookingReportWebApplicationFactory
    : WebApplicationFactory<Program>
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
        builder.UseSetting("REDIS_URL", "localhost:6379,abortConnect=false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>();
            services.RemoveAll<DbContextOptions<BookingDbContext>>();
            services.RemoveAll<BookingDbContext>();
            services.RemoveAll<VietRideDbContextBase>();
            services.RemoveAll<IConnectionMultiplexer>();

            services.AddSingleton(_ =>
            {
                var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
                BookingDbContext.ConfigurePostgresTypes(dataSourceBuilder);
                return dataSourceBuilder.Build();
            });
            services.AddDbContext<BookingDbContext>((provider, options) =>
                options.UseNpgsql(
                    provider.GetRequiredService<NpgsqlDataSource>(),
                    npgsql => npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        BookingDbContext.SchemaName)));
            services.AddScoped<VietRideDbContextBase>(
                provider => provider.GetRequiredService<BookingDbContext>());
            services.AddSingleton<IConnectionMultiplexer>(InMemoryIdempotencyRedis.Create());
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
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
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
        client.DefaultRequestHeaders.Add(
            "X-Internal-Auth",
            $"Bearer {MintInternalJwt()}");
        return client;
    }

    public async Task ResetAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE vietride_booking.bookings CASCADE;");
    }

    public async Task<Guid> SeedBookingAsync(
        Guid operatorId,
        BookingStatus status,
        DateTimeOffset? completedAt,
        long totalAmount,
        Guid? tripId = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var booking = VietRide.Booking.Domain.Entities.Booking.CreatePendingPayment(
            BookingCode.Generate(DateTimeOffset.UtcNow),
            Guid.NewGuid(),
            tripId ?? Guid.NewGuid(),
            operatorId,
            Guid.NewGuid(),
            null,
            null,
            null,
            Money.FromRaw(totalAmount),
            Money.Zero,
            Money.FromRaw(totalAmount),
            "Origin",
            "Destination",
            DateTimeOffset.UtcNow.AddDays(1));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_booking.bookings
            SET status = CAST({status.ToString()} AS public.booking_status),
                completed_at = {completedAt}
            WHERE id = {booking.Id};
            """);
        return booking.Id;
    }

    public async Task<(int First, int Replay)> CompleteTripTwiceAsync(
        Guid tripId,
        DateTimeOffset completedAt)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var handler = new HandleTripCompletedCommandHandler(
            provider.GetRequiredService<IBookingRepository>(),
            provider.GetRequiredService<IBookingStatusHistoryRepository>());
        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        var command = new HandleTripCompletedCommand(tripId, completedAt, HasSubstitution: false);
        var first = await unitOfWork.ExecuteInTransactionAsync(
            () => handler.Handle(command, CancellationToken.None),
            CancellationToken.None);
        var replay = await unitOfWork.ExecuteInTransactionAsync(
            () => handler.Handle(command, CancellationToken.None),
            CancellationToken.None);
        return (first, replay);
    }

    public async Task<int> CountCompletionHistoryAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        return await db.BookingStatusHistories.AsNoTracking().CountAsync(
            row => row.Source == "COMPLETE_ON_TRIP_COMPLETED");
    }

    public async Task DeletePlatformProjectionAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM vietride_booking.platform_booking_stats;");
    }

    public async Task<long> RunPlatformBackfillTwiceAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT vietride_booking.rebuild_platform_booking_stats(); " +
            "SELECT vietride_booking.rebuild_platform_booking_stats();");
        return await db.Database.SqlQueryRaw<long>(
                "SELECT COUNT(*)::bigint AS \"Value\" " +
                "FROM vietride_booking.platform_booking_stats")
            .SingleAsync();
    }

    public async Task MigrateAsync(string? targetMigration = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    public async Task<bool> ReportIndexExistsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        return await db.Database.SqlQueryRaw<bool>(
                "SELECT to_regclass('vietride_booking.idx_bookings_completed_report') " +
                "IS NOT NULL AS \"Value\"")
            .SingleAsync();
    }

    public async Task SeedPlannerFixtureAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO vietride_booking.bookings (
                id, booking_code, passenger_user_id, trip_id, operator_id,
                pickup_station_id, base_fare, discount_amount, total_amount,
                status, completed_at)
            SELECT gen_random_uuid(),
                   'VR-20260717-' || lpad(series::text, 8, '0'),
                   gen_random_uuid(),
                   gen_random_uuid(),
                   '40000000-0000-0000-0000-000000000099'::uuid,
                   gen_random_uuid(),
                   100000,
                   0,
                   100000,
                   CASE WHEN series <= 2000
                       THEN 'COMPLETED'::public.booking_status
                       ELSE 'CONFIRMED'::public.booking_status
                   END,
                   CASE WHEN series <= 2000
                       THEN '2026-07-01T00:00:00Z'::timestamptz
                           + ((series - 1) % 30) * interval '1 day'
                       ELSE NULL
                   END
            FROM generate_series(1, 32000) AS series;
            ANALYZE vietride_booking.bookings;
            """);
    }

    public async Task<string> ExplainReportQueryAsync(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            EXPLAIN
            SELECT operator_id, COUNT(*)::numeric, SUM(total_amount)::numeric
            FROM vietride_booking.bookings
            WHERE status = 'COMPLETED'::public.booking_status
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
            Environment.GetEnvironmentVariable("VIETRIDE_BOOKING_TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=unused;Username=vietride;Password=vietride_dev";
        return new NpgsqlConnectionStringBuilder(configured)
        {
            Database = $"vietride_booking_platform_report_{Guid.NewGuid():N}",
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
