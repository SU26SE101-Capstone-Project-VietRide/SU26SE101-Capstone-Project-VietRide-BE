using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Outbox;
using Xunit;

namespace VietRide.Shared.Persistence.UnitTests.Outbox;

/// <summary>
/// Spins up a throwaway Postgres database (mirrors the Identity
/// DbBackedAuthFactory create/migrate/drop pattern, but uses EnsureCreated
/// since the shared library owns no migrations), builds a data source with the
/// outbox_event_status enum mapped, and exposes a frozen clock so CreatedAt /
/// PublishedAt conversions are deterministic.
/// </summary>
public sealed class OutboxStoreFixture : IAsyncLifetime
{
    private readonly string _databaseName;
    private readonly string _connectionString;
    private NpgsqlDataSource _dataSource = null!;
    private bool _created;

    public OutboxStoreFixture()
    {
        var baseConn = Environment.GetEnvironmentVariable("VIETRIDE_PERSISTENCE_TEST_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=vietride;Password=vietride_dev";

        _databaseName = $"vietride_outbox_tests_{Guid.NewGuid():N}";
        _connectionString = new NpgsqlConnectionStringBuilder(baseConn)
        {
            Database = _databaseName,
        }.ConnectionString;
    }

    public IClock Clock { get; } = new FrozenClock(
        new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync();

        var builder = new NpgsqlDataSourceBuilder(_connectionString);
        builder.MapEnum<OutboxEventStatus>("outbox_event_status", new NpgsqlNullNameTranslator());
        _dataSource = builder.Build();

        await using var ctx = CreateContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await DropDatabaseAsync();
    }

    public OutboxTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseNpgsql(_dataSource)
            .Options;
        return new OutboxTestDbContext(options, Clock);
    }

    public OutboxStore CreateStore(OutboxTestDbContext ctx) => new(ctx, Clock);

    /// Truncate the outbox table so each test runs against an isolated state.
    public async Task ResetAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE outbox_events RESTART IDENTITY CASCADE;");
    }

    private async Task CreateDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
        await command.ExecuteNonQueryAsync();
        _created = true;
    }

    private async Task DropDatabaseAsync()
    {
        if (!_created)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
        await connection.OpenAsync();
        await using var terminate = connection.CreateCommand();
        terminate.CommandText =
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid();";
        terminate.Parameters.AddWithValue("db", _databaseName);
        await terminate.ExecuteNonQueryAsync();

        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
        await drop.ExecuteNonQueryAsync();
        _created = false;
    }

    private string BuildMaintenanceConnectionString()
        => new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres" }.ConnectionString;

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }
}
