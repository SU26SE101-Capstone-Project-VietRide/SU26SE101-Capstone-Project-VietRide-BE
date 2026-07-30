using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests;

public sealed class OperatorParcelDetailPersistenceIntegrationTests
{
    [Fact]
    public async Task Repository_ReturnsTenantParcelAndOrderedHistoryInOneQuery()
    {
        var databaseName = $"vietride_parcel_ui14_detail_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var operatorId = Guid.NewGuid();
            var owned = CreateParcel(operatorId, "OWNED");
            var other = CreateParcel(Guid.NewGuid(), "OTHER");
            await using (var seed = CreateDbContext(dataSource))
            {
                await seed.Database.MigrateAsync();
                seed.Parcels.AddRange(owned, other);
                await seed.SaveChangesAsync();
                await seed.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE vietride_parcel.parcels
                    SET status = 'CHECKED_IN'::vietride_parcel.parcel_status
                    WHERE id = {owned.Id};
                    UPDATE vietride_parcel.parcels
                    SET status = 'CANCELLED'::vietride_parcel.parcel_status,
                        cancellation_reason = {"operator-cancelled"}
                    WHERE id = {owned.Id};
                    """);
            }

            var counter = new SelectCommandCounter();
            await using var context = CreateDbContext(dataSource, counter);
            var repository = CreateRepository(context);
            counter.Reset();

            var detail = await repository.GetOperatorDetailAsync(
                owned.Id,
                operatorId,
                CancellationToken.None);

            detail.Should().NotBeNull();
            detail!.Parcel.Id.Should().Be(owned.Id);
            detail.StatusHistory.Select(history => history.Status).Should().Equal(
                ParcelStatus.CHECKED_IN,
                ParcelStatus.CANCELLED);
            detail.StatusHistory.Last().Reason.Should().Be("operator-cancelled");
            counter.Count.Should().Be(1);

            counter.Reset();
            var masked = await repository.GetOperatorDetailAsync(
                other.Id,
                operatorId,
                CancellationToken.None);
            masked.Should().BeNull();
            counter.Count.Should().Be(1);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static IParcelRepository CreateRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
            throwOnError: true)!;
        return (IParcelRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static ParcelEntity CreateParcel(Guid operatorId, string marker)
        => ParcelEntity.CreatePendingPayment(
            ($"VRP-UI14-{marker}-{Guid.NewGuid():N}")[..30],
            Guid.NewGuid(),
            null,
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.test",
            operatorId,
            Guid.NewGuid(),
            null,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(
        NpgsqlDataSource dataSource,
        DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ParcelDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ParcelDbContext.SchemaName));
        if (interceptor is not null)
            builder.AddInterceptors(interceptor);
        return new ParcelDbContext(builder.Options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new NpgsqlConnectionStringBuilder(
            connectionString.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase))
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SelectCommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
