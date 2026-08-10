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

public sealed class OperatorParcelStatsPersistenceIntegrationTests
{
    [Fact]
    public async Task OperatorParcelStats_RepositoryUsesTenantVietnamRangeStatusAndHistoricalRouteSnapshot()
    {
        var databaseName = $"vietride_parcel_ui16_stats_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var operatorId = Guid.NewGuid();
            var otherOperatorId = Guid.NewGuid();
            var routeA = Guid.NewGuid();
            var routeB = Guid.NewGuid();
            var before = CreateParcel(operatorId, routeA, "Outside Before");
            var routeAFirst = CreateParcel(operatorId, routeA, "Tuyến lịch sử A cũ");
            var routeASecond = CreateParcel(operatorId, routeA, "Tuyến lịch sử A mới");
            var routeBInside = CreateParcel(operatorId, routeB, "Tuyến lịch sử B");
            var after = CreateParcel(operatorId, routeB, "Outside After");
            var otherTenant = CreateParcel(otherOperatorId, routeA, "Other Tenant");

            await using (var seed = CreateDbContext(dataSource))
            {
                await seed.Database.MigrateAsync();
                seed.Parcels.AddRange(before, routeAFirst, routeASecond, routeBInside, after, otherTenant);
                await seed.SaveChangesAsync();
                await SetStateAsync(seed, before.Id, ParcelStatus.IN_TRANSIT, "2026-01-31T16:59:59Z");
                await SetStateAsync(seed, routeAFirst.Id, ParcelStatus.IN_TRANSIT, "2026-01-31T17:00:00Z");
                await SetStateAsync(seed, routeASecond.Id, ParcelStatus.IN_TRANSIT, "2026-02-01T16:59:59Z");
                await SetStateAsync(seed, routeBInside.Id, ParcelStatus.CANCELLED, "2026-02-01T08:00:00Z");
                await SetStateAsync(seed, after.Id, ParcelStatus.CANCELLED, "2026-02-01T17:00:00Z");
                await SetStateAsync(seed, otherTenant.Id, ParcelStatus.IN_TRANSIT, "2026-02-01T08:00:00Z");
            }

            var counter = new SelectCommandCounter();
            await using var context = CreateDbContext(dataSource, counter);
            var repository = CreateRepository(context);
            var fromUtc = DateTimeOffset.Parse("2026-01-31T17:00:00Z");
            var toUtc = DateTimeOffset.Parse("2026-02-01T17:00:00Z");

            counter.Reset();
            var byStatus = await repository.GetAsync(operatorId, fromUtc, toUtc, "status", 10);
            counter.Count.Should().Be(1);
            byStatus.TotalParcels.Should().Be(3);
            byStatus.Buckets.Should().ContainSingle(bucket => bucket.Key == "IN_TRANSIT" && bucket.Count == 2);
            byStatus.Buckets.Should().ContainSingle(bucket => bucket.Key == "CANCELLED" && bucket.Count == 1);

            counter.Reset();
            var byRoute = await repository.GetAsync(operatorId, fromUtc, toUtc, "route", 2);
            counter.Count.Should().Be(1);
            byRoute.TotalParcels.Should().Be(3);
            byRoute.Buckets.Should().HaveCount(2);
            byRoute.Buckets[0].RouteId.Should().Be(routeA);
            byRoute.Buckets[0].RouteName.Should().Be("Tuyến lịch sử A mới");
            byRoute.Buckets[0].Count.Should().Be(2);
            byRoute.Buckets[1].RouteId.Should().Be(routeB);
            byRoute.Buckets[1].RouteName.Should().Be("Tuyến lịch sử B");
            byRoute.Buckets[1].Count.Should().Be(1);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static IOperatorParcelStatsRepository CreateRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.OperatorParcelStatsRepository",
            throwOnError: true)!;
        return (IOperatorParcelStatsRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static ParcelEntity CreateParcel(Guid operatorId, Guid routeId, string routeName)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            ($"VRP-UI16-{Guid.NewGuid():N}")[..30],
            Guid.NewGuid(),
            null,
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            null,
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
        parcel.CaptureTripDisplaySnapshot(
            routeId,
            routeName,
            "Origin",
            "Destination",
            Guid.NewGuid(),
            "51A-12345");
        return parcel;
    }

    private static Task SetStateAsync(
        ParcelDbContext dbContext,
        Guid parcelId,
        ParcelStatus status,
        string createdAt)
        => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_parcel.parcels
            SET status = CAST({status.ToString()} AS vietride_parcel.parcel_status),
                created_at = {DateTimeOffset.Parse(createdAt)},
                updated_at = {DateTimeOffset.Parse(createdAt)}
            WHERE id = {parcelId};
            """);

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
        var options = ParcelIntegrationDbContextOptions.Create(dataSource, interceptor);
        return new ParcelDbContext(options, new SystemClock());
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
            if (command.CommandText.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                Count++;
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
