using System.Data.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Booking.IntegrationTests;

/// <summary>
/// Real-Postgres integration tests that exercise the voucher persistence path end-to-end.
/// Each test class boots a throwaway database, runs <c>db.Database.MigrateAsync()</c>
/// (real EF migrations), then verifies voucher-enum and voucher-write behaviour.
///
/// Regression guard: the Day-14 migration bug created each voucher PG enum in two schemas
/// (vietride_booking + public) which caused Npgsql to throw "More than one PostgreSQL type
/// was found with the name voucher_funding_type" on every voucher write. These tests would
/// have caught that before it reached CI.
/// </summary>
[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class VoucherPersistenceIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly DbBackedVoucherFactory _factory;

    public VoucherPersistenceIntegrationTests(DbBackedVoucherFactory factory)
    {
        _factory = factory;
    }

    // -----------------------------------------------------------------------
    // Test 1 (regression guard): each voucher enum must exist in exactly ONE
    // schema after migrations run. This is the direct guard against the
    // Day-14 "More than one PostgreSQL type" bug.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Migrations_VoucherFundingTypeEnum_ExistsInExactlyOneSchema()
    {
        await _factory.InitializeAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var count = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*)::bigint AS \"Value\" " +
                "FROM pg_type t " +
                "JOIN pg_namespace n ON n.oid = t.typnamespace " +
                "WHERE t.typname = 'voucher_funding_type'")
            .SingleAsync();

        count.Should().Be(1, because:
            "voucher_funding_type must be created in exactly one schema — " +
            "the Day-14 migration bug created it in two schemas causing HTTP 500");
    }

    [Fact]
    public async Task Migrations_VoucherTypeEnum_ExistsInExactlyOneSchema()
    {
        await _factory.InitializeAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var count = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*)::bigint AS \"Value\" " +
                "FROM pg_type t " +
                "JOIN pg_namespace n ON n.oid = t.typnamespace " +
                "WHERE t.typname = 'voucher_type'")
            .SingleAsync();

        count.Should().Be(1, because:
            "voucher_type must be created in exactly one schema");
    }

    [Fact]
    public async Task Migrations_OperatorVoucherConsentStatusEnum_ExistsInExactlyOneSchema()
    {
        await _factory.InitializeAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var count = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*)::bigint AS \"Value\" " +
                "FROM pg_type t " +
                "JOIN pg_namespace n ON n.oid = t.typnamespace " +
                "WHERE t.typname = 'operator_voucher_consent_status'")
            .SingleAsync();

        count.Should().Be(1, because:
            "operator_voucher_consent_status must be created in exactly one schema");
    }

    // -----------------------------------------------------------------------
    // Test 2 (enum WRITE path): insert a VIETRIDE_FUNDED voucher through the
    // REAL VoucherRepository, SaveChanges, reload, and assert FundingType
    // round-trips correctly. This exercises the exact enum WRITE path that
    // returned HTTP 500 in production.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VoucherRepository_AddAndReload_VietrideFundedVoucher_RoundTripsCorrectly()
    {
        await _factory.InitializeAsync();

        var createdByUserId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var voucher = Voucher.Create(
            code: $"TEST-{Guid.NewGuid():N}"[..20],
            name: "Test VIETRIDE_FUNDED Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 50_000,
            minOrderAmount: Money.FromRaw(100_000),
            maxDiscountAmount: null,
            totalUsageLimit: 100,
            perUserLimit: 1,
            validFrom: now,
            validUntil: now.AddDays(30),
            applicableOperatorIds: null,
            applicableRouteIds: null,
            fundingType: VoucherFundingType.VIETRIDE_FUNDED,
            ownerOperatorId: null,
            createdByUserId: createdByUserId);

        await using (var writeScope = _factory.Services.CreateAsyncScope())
        {
            var repo = writeScope.ServiceProvider.GetRequiredService<IVoucherRepository>();
            var db = writeScope.ServiceProvider.GetRequiredService<BookingDbContext>();

            await repo.AddAsync(voucher, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        Voucher? reloaded;
        await using (var readScope = _factory.Services.CreateAsyncScope())
        {
            var repo = readScope.ServiceProvider.GetRequiredService<IVoucherRepository>();
            reloaded = await repo.GetByIdAsync(voucher.Id, CancellationToken.None);
        }

        reloaded.Should().NotBeNull();
        reloaded!.FundingType.Should().Be(VoucherFundingType.VIETRIDE_FUNDED,
            because: "the enum value must survive a PG write + read round-trip");
        reloaded.Type.Should().Be(VoucherType.FIXED_AMOUNT);
        reloaded.Code.Should().Be(voucher.Code);
        reloaded.Value.Should().Be(50_000);
    }

    // -----------------------------------------------------------------------
    // Test 3 (error guard): inserting an operator-owned voucher with
    // VIETRIDE_FUNDED must throw at the domain layer, NOT in the DB.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VoucherCreate_OperatorOwned_WithVietrideFundingType_ThrowsArgumentException()
    {
        await _factory.InitializeAsync();

        var act = () => Voucher.Create(
            code: "OPERATOR-BAD",
            name: "Bad Voucher",
            type: VoucherType.PERCENT_OFF,
            value: 10,
            minOrderAmount: Money.FromRaw(0),
            maxDiscountAmount: null,
            totalUsageLimit: null,
            perUserLimit: null,
            validFrom: DateTimeOffset.UtcNow,
            validUntil: DateTimeOffset.UtcNow.AddDays(7),
            applicableOperatorIds: null,
            applicableRouteIds: null,
            fundingType: VoucherFundingType.VIETRIDE_FUNDED,
            ownerOperatorId: Guid.NewGuid(),   // operator-owned
            createdByUserId: Guid.NewGuid());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*OPERATOR_FUNDED*");
    }

    // -----------------------------------------------------------------------
    // Inner factory — DB lifecycle (mirrors Identity DbBackedLifecycleFactory)
    // -----------------------------------------------------------------------

    public sealed class DbBackedVoucherFactory
        : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        private readonly string _connectionString = BuildTestDatabaseConnectionString();
        private readonly string _databaseName;
        private bool _databaseCreated;
        private bool _initialized;

        public SqlCaptureInterceptor SqlCapture { get; } = new();

        public DbBackedVoucherFactory()
        {
            _databaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database!;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("REDIS_URL", "localhost:6379,abortConnect=false");

            builder.ConfigureServices(services =>
            {
                // Remove existing Npgsql data source and DbContext registrations so we can
                // point them at the throwaway test database (mirrors Identity fixture pattern).
                services.RemoveAll<NpgsqlDataSource>();
                services.RemoveAll<DbContextOptions<BookingDbContext>>();
                services.RemoveAll<BookingDbContext>();
                services.RemoveAll<VietRideDbContextBase>();

                services.AddSingleton(_ =>
                {
                    var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
                    dataSourceBuilder.MapEnum<OutboxEventStatus>(
                        $"{BookingDbContext.SchemaName}.outbox_event_status",
                        new NpgsqlNullNameTranslator());
                    // Register all booking enum mappings (same as BookingDbContext.ConfigurePostgresTypes).
                    BookingDbContext.ConfigurePostgresTypes(dataSourceBuilder);
                    return dataSourceBuilder.Build();
                });

                services.AddDbContext<BookingDbContext>((sp, options) =>
                {
                    options
                        .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
                        .AddInterceptors(SqlCapture)
                        .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
                });

                services.AddScoped<VietRideDbContextBase>(
                    sp => sp.GetRequiredService<BookingDbContext>());
            });
        }

        public sealed class SqlCaptureInterceptor : DbCommandInterceptor
        {
            private readonly object _gate = new();
            private readonly List<string> _commands = [];

            public IReadOnlyList<string> Commands
            {
                get
                {
                    lock (_gate)
                    {
                        return [.. _commands];
                    }
                }
            }

            public void Clear()
            {
                lock (_gate)
                {
                    _commands.Clear();
                }
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    _commands.Add(command.CommandText);
                }

                return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
            }
        }

        /// <summary>
        /// Creates the throwaway DB, runs all EF migrations, and reloads PG type cache.
        /// Idempotent — safe to call from every test.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            await CreateDatabaseAsync();

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await db.Database.MigrateAsync();
            await ReloadPostgresTypesAsync();
            _initialized = true;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await DropDatabaseAsync();
        }

        // -----------------------------------------------------------------------
        // DB lifecycle helpers (direct mirror of Identity fixture)
        // -----------------------------------------------------------------------

        private async Task CreateDatabaseAsync()
        {
            if (_databaseCreated)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            await command.ExecuteNonQueryAsync();
            _databaseCreated = true;
        }

        private async Task DropDatabaseAsync()
        {
            if (!_databaseCreated)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
            await connection.OpenAsync();

            await using var terminateCommand = connection.CreateCommand();
            terminateCommand.CommandText =
                "SELECT pg_terminate_backend(pid) " +
                "FROM pg_stat_activity " +
                "WHERE datname = @databaseName AND pid <> pg_backend_pid();";
            terminateCommand.Parameters.AddWithValue("databaseName", _databaseName);
            await terminateCommand.ExecuteNonQueryAsync();

            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
            await dropCommand.ExecuteNonQueryAsync();
            _databaseCreated = false;
        }

        private async Task ReloadPostgresTypesAsync()
        {
            var dataSource = Services.GetRequiredService<NpgsqlDataSource>();
            await using var connection = await dataSource.OpenConnectionAsync();
            await connection.ReloadTypesAsync();
        }

        private string BuildMaintenanceConnectionString()
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Database = "postgres",
            };

            return builder.ConnectionString;
        }

        private static string BuildTestDatabaseConnectionString()
        {
            var configured =
                Environment.GetEnvironmentVariable("VIETRIDE_BOOKING_TEST_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                ?? "Host=localhost;Port=5432;Database=vietride_booking_tests;Username=vietride;Password=vietride_dev";

            var builder = new NpgsqlConnectionStringBuilder(configured)
            {
                Database = $"vietride_booking_voucher_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }
    }
}
